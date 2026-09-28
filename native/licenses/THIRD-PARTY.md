# Bundled OCR components

Only the on-demand `tools/ocr/CutTool.Ocr.exe` helper loads these components.

- Tesseract .NET wrapper 5.2.0: Copyright 2012-2022 Charles Weld;
  https://github.com/charlesw/tesseract ; Apache-2.0.
- Tesseract OCR 5.2.0 native runtime: https://github.com/tesseract-ocr/tesseract ; Apache-2.0.
- English `tessdata_fast` model, revision `87416418657359cb625c412a48b6e1d6d41c29bd`:
  https://github.com/tesseract-ocr/tessdata_fast ; Apache-2.0.
- Leptonica 1.82.0: https://github.com/DanBloomberg/leptonica ; BSD-style license
  reproduced in `Leptonica-LICENSE.txt`.
- InteropDotNet (embedded in the wrapper): Copyright 2014 Andrey Akinshin;
  https://github.com/AndreyAkinshin/InteropDotNet ; MIT, reproduced in `InteropDotNet-LICENSE.txt`.

The Apache-2.0 license is reproduced in `Apache-2.0.txt`. These components are
distributed unmodified. Dependency URLs and checksums are in `native/restore-ocr.ps1`.
