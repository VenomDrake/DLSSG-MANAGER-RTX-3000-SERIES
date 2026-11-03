"""Regenerate the original application icon (developer-only Pillow dependency)."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
image = Image.new("RGBA", (768, 768), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((0, 0, 767, 767), radius=174, fill="#12352e")
draw.rounded_rectangle((117, 162, 495, 438), radius=36, outline="#70e5bb", width=42)
draw.rounded_rectangle((273, 330, 651, 606), radius=36, fill="#12352e", outline="#70e5bb", width=42)
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(root / "src/DlssgManager.Desktop/Assets/app.ico", sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
