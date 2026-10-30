"""Crop a window screenshot to the app's client area (the size the README uses)."""
import sys
from PIL import Image

source, target = sys.argv[1], sys.argv[2]
image = Image.open(source)
image.crop((4, 32, image.width - 4, image.height - 8)).save(target)
print(target, Image.open(target).size)
