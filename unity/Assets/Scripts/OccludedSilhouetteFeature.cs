using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// URP renderer feature that draws an X-RAY OCCLUSION SILHOUETTE of the player: wherever the player's mesh is
/// hidden behind opaque geometry (house / tree / wall), a flat coloured silhouette shows through so the player
/// is never lost behind scenery. Companion to <see cref="CameraOccluderFader"/> — that fades the occluder; this
/// guarantees the player stays readable regardless of the fade.
///
/// <para>How it works: after transparents, it re-draws the objects tagged by <see cref="OccludedSilhouetteTarget"/>
/// (matched via their <see cref="Renderer.renderingLayerMask"/> bit, so it's player-only without touching the
/// shared crowd physics layer) using the <c>Unwritten/OccludedSilhouette</c> override material. That material has
/// THREE passes (clear a stencil bit over the player's area → mark where the player is actually visible → paint the
/// silhouette colour only where the player is present but NOT visible = hidden behind scene geometry). Deciding by
/// screen AREA rather than per-fragment depth is what avoids the player self-occluding (see the shader header).</para>
///
/// Register once via <b>Tools ▸ unwritten ▸ Scene Setup ▸ Setup Occluded Silhouette</b> (adds this to the URP
/// renderer, mirroring <see cref="HandDrawnGradeFeature"/>).
/// </summary>
[DisallowMultipleComponent]
public class OccludedSilhouetteFeature : ScriptableRendererFeature
{
    [Tooltip("Colour (and alpha) of the see-through silhouette. Alpha < 1 = ghostly; 1 = solid.")]
    public Color silhouetteColor = new Color(0.55f, 0.8f, 1f, 1f);

    [Tooltip("Runtime master switch (a DevSettings toggle can flip this).")]
    public static bool Enabled = true;

    static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");

    Material material;
    SilhouettePass pass;

    public override void Create()
    {
        var sh = Shader.Find("Unwritten/OccludedSilhouette");
        if (sh == null) return;   // editor: found. build: add to Always Included Shaders (the setup tool does this).
        if (material == null) material = CoreUtils.CreateEngineMaterial(sh);
        pass = new SilhouettePass(material)
        {
            // After transparents so the silhouette paints over BOTH opaque occluders and the faded (transparent) ones.
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!Enabled || pass == null || material == null) return;
        var camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;   // skip reflection/preview cams

        material.SetColor(OutlineColorID, silhouetteColor);
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing)
    {
        if (material != null) CoreUtils.Destroy(material);
        material = null;
    }

    // ---- the pass: re-draw the player-tagged renderers three times (clear-bit / mark-visible / paint) ----
    class SilhouettePass : ScriptableRenderPass
    {
        readonly Material overrideMat;
        readonly List<ShaderTagId> tags = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalForward"),   // the player's lit model
            new ShaderTagId("SRPDefaultUnlit"),
        };

        public SilhouettePass(Material m)
        {
            overrideMat = m;
        }

        // One RendererList per shader pass (clear / mark-visible / paint), drawn in order into the same target.
        class PassData { public RendererListHandle clear, mark, paint; }

        static void Execute(PassData d, RasterGraphContext ctx)
        {
            ctx.cmd.DrawRendererList(d.clear);
            ctx.cmd.DrawRendererList(d.mark);
            ctx.cmd.DrawRendererList(d.paint);
        }

        // Cached to avoid a per-frame delegate allocation (SetRenderFunc otherwise closes over Execute each call).
        static readonly BaseRenderFunc<PassData, RasterGraphContext> ExecuteFn = Execute;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData    = frameData.Get<UniversalCameraData>();
            var lightData     = frameData.Get<UniversalLightData>();
            var resourceData  = frameData.Get<UniversalResourceData>();

            if (!resourceData.activeColorTexture.IsValid() || !resourceData.activeDepthTexture.IsValid()) return;

            var draw = RenderingUtils.CreateDrawingSettings(tags, renderingData, cameraData, lightData,
                                                            cameraData.defaultOpaqueSortFlags);
            draw.overrideMaterial = overrideMat;

            var filter = new FilteringSettings(RenderQueueRange.all);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Occluded Silhouette", out var passData))
            {
                var cull = renderingData.cullResults;
                // Clear + MarkVisible cover the whole player unit (body + held equipment) so gear hides the body
                // behind it; Paint draws ONLY the body, so equipment is never x-rayed.
                draw.overrideMaterialPassIndex = 0;   // ClearArea
                filter.renderingLayerMask = OccludedSilhouetteTarget.CoverBit;
                passData.clear = renderGraph.CreateRendererList(new RendererListParams(cull, draw, filter));
                draw.overrideMaterialPassIndex = 1;   // MarkVisible
                filter.renderingLayerMask = OccludedSilhouetteTarget.CoverBit;
                passData.mark  = renderGraph.CreateRendererList(new RendererListParams(cull, draw, filter));
                draw.overrideMaterialPassIndex = 2;   // PaintOccluded
                filter.renderingLayerMask = OccludedSilhouetteTarget.Bit;
                passData.paint = renderGraph.CreateRendererList(new RendererListParams(cull, draw, filter));

                builder.UseRendererList(passData.clear);
                builder.UseRendererList(passData.mark);
                builder.UseRendererList(passData.paint);

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                // Bind the live scene depth+stencil (read-write, ZWrite Off): the mark pass depth-tests against the
                // scene depth to find where the player is visible, and all three passes read/write our stencil bit.
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                builder.SetRenderFunc(ExecuteFn);
            }
        }
    }
}
