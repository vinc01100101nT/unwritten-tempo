using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

/// <summary>
/// URP renderer feature that applies the HAND-DRAWN colour grade (saturation / contrast). It blits the camera colour
/// through <c>Unwritten/HandDrawnGrade</c> just before post-processing, reading the live values published by
/// <see cref="HandDrawnFX"/> (the static <see cref="HandDrawnFX.GradeActive"/> etc.). We use a renderer feature
/// rather than a Color-Adjustments volume because a volume only takes effect when the camera has post-processing on
/// AND its Volume-Mask matches — both easy to get wrong per project. A feature always runs once registered.
///
/// Register it once via <b>Tools ▸ unwritten ▸ Setup Hand-Drawn Grade</b> (adds this to the URP renderer). It is
/// genuinely zero-cost when off: <see cref="HandDrawnFX.GradeActive"/> is false at full saturation / zero contrast,
/// so the pass early-outs and no blit is recorded.
/// </summary>
[DisallowMultipleComponent]
public class HandDrawnGradeFeature : ScriptableRendererFeature
{
    Material material;
    GradePass pass;

    static readonly int SaturationID = Shader.PropertyToID("_Saturation");
    static readonly int ContrastID   = Shader.PropertyToID("_Contrast");

    public override void Create()
    {
        var sh = Shader.Find("Unwritten/HandDrawnGrade");
        if (sh == null)
        {
            // Editor: usually found. Build: add the shader to Always Included Shaders.
            return;
        }
        if (material == null) material = CoreUtils.CreateEngineMaterial(sh);
        pass = new GradePass(material)
        {
            // After transparents (so it grades world + sprites) but before URP's own post stack.
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null || material == null) return;
        if (!HandDrawnFX.GradeActive) return;                         // nothing to do — stay free
        var camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;   // skip reflection/preview cams

        material.SetFloat(SaturationID, HandDrawnFX.GradeSaturation);
        material.SetFloat(ContrastID,   HandDrawnFX.GradeContrast);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (material != null) CoreUtils.Destroy(material);
        material = null;
    }

    // ---- the pass: blit camera colour → temp → back through the grade material ----
    class GradePass : ScriptableRenderPass
    {
        readonly Material mat;
        public GradePass(Material m) { mat = m; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;   // can't read+write the backbuffer directly

            TextureHandle source = resources.activeColorTexture;
            if (!source.IsValid()) return;

            // A blit can't read and write the same texture, so grade into a temp, then COPY the temp back into the
            // real camera-colour target. We copy back (rather than reassign cameraColor = temp) because when post-
            // processing is off nothing downstream consumes a reassigned handle, so the Render Graph culls the pass
            // and the screen goes black. Writing into `source` — which the final blit always reads — can't be culled.
            var desc = renderGraph.GetTextureDesc(source);
            desc.name = "HandDrawnGradeTemp";
            desc.clearBuffer = false;
            desc.depthBufferBits = 0;
            TextureHandle temp = renderGraph.CreateTexture(desc);

            // Pass 0: source → temp (apply saturation/contrast).
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, temp, mat, 0), "HandDrawnGrade");
            // Pass 1: temp → source (plain copy back into the camera colour the rest of the frame uses).
            renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(temp, source, mat, 1), "HandDrawnGradeCopy");
        }
    }
}
