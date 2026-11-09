# Banner

`banner.html` is the source of `docs/images/banner.png`. It uses CSS only, with no game assets and no SI or FM logos. Re-render it with headless Chrome.

```
google-chrome --headless=new --hide-scrollbars --default-background-color=00000000 \
  --force-device-scale-factor=2 --window-size=1280,400 --virtual-time-budget=8000 \
  --screenshot=docs/images/banner.png "file://$PWD/docs/banner/banner.html"
```

Then scale it to 1920×600 (`--force-device-scale-factor=1.5` does the same in one step).
