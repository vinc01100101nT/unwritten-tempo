@echo off
cd /d C:\Hunyuan3D-2

call venv312\Scripts\activate

python gradio_app.py ^
--enable_t23d ^
--low_vram_mode

pause