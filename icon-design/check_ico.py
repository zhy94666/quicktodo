import sys
from PIL import Image
for p in ["icon-design/../MyTodo/Assets/app.ico", "icon-design/../MyTodo/Assets/tray.ico"]:
    im = Image.open(p)
    print(p.split("/")[-1], "sizes:", sorted(im.info.get("sizes", [])))
