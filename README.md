# ocr-demo – porównanie silników OCR w Dockerze (.NET 10)

Demo porównuje silniki OCR, które mogą zastąpić ABBYY FineReader Engine 12 (Windows) w aplikacji
uruchamianej w Dockerze. Każdy silnik działa w kontenerze Linux **tylko na CPU**. Dla każdego dokumentu
demo zapisuje:

- tekst (w kolejności czytania wyznaczonej przez silnik),
- tabele (HTML + CSV) – jeśli silnik je rozpoznaje,
- **PDF z niewidoczną warstwą tekstową** (searchable PDF),
- czas przetwarzania i jakość (CER/WER) względem tekstu wzorcowego.

## Silniki

| Nazwa w raporcie | Silnik (wersja) | Tekst | Tabele | Warstwa tekstowa PDF | Licencja |
|---|---|---|---|---|---|
| `ocrmypdf` | OCRmyPDF 17.14.2 + Tesseract 5.5.4 (`tessdata_best` pol+eng) | linie + słowa | – | natywna (OCRmyPDF) | MPL-2.0 / Apache-2.0 ¹ |
| `paddleocr` | PaddleOCR 3.7.0, PP-StructureV3, modele PP-OCRv6 (Paddle 3.3.1) | linie | ✔ (HTML) | generowana | Apache-2.0 |
| `doctr` | docTR 1.2.0 (PyTorch CPU), rozpoznawanie: `parseq-multilingual-v1` | linie + słowa | ✔ (od 1.2) | generowana | Apache-2.0 |
| `docling-rapidocr` | Docling Serve 1.39.0 (docling 2.152.0), OCR: RapidOCR (PP-OCRv6 ONNX) | akapity | ✔ (TableFormer) | generowana, przybliżona ² | MIT / Apache-2.0 |
| `docling-easyocr` | j.w., OCR: EasyOCR (`latin_g2`) | akapity | ✔ (TableFormer) | generowana, przybliżona ² | MIT / Apache-2.0 |

¹ Obraz OCRmyPDF zawiera m.in. Ghostscript (AGPL-3.0), uruchamiany jako osobny proces – do oceny
prawnej przed użyciem produkcyjnym.
² Docling zwraca ramki akapitów, nie słów – tekst akapitu jest rozkładany na szacowaną liczbę linii.

Poza zakresem (decyzja z 2026-11-25): silniki komercyjne (Aspose.OCR, IronOCR, GdPicture, LEADTOOLS)
oraz ABBYY FRE for Linux (brak dostępu do wersji linuksowej).

## Wymagania

- Docker Desktop / Docker Engine z Compose v2 (kontenery Linux, x86-64)
- ok. 20 GB miejsca na obrazy (Docling ~5,5 GB, PaddleOCR i docTR po kilka GB)
- .NET SDK 10 – tylko do uruchamiania runnera lokalnie / testów

## Szybki start

```bash
# 1. Skopiuj dokumenty do input_documents/ (opcjonalnie wzorce <nazwa>.gt.txt)

# 2. Wszystko naraz: silniki + jednorazowy przebieg runnera (pierwsze budowanie trwa kilkanaście minut).
#    Runner czeka na healthcheck silników, przetwarza dokumenty i kończy działanie; silniki działają dalej.
docker compose up --build

# Kolejne przebiegi na działających silnikach
docker compose run --rm runner
docker compose run --rm runner --engines doctr,ocrmypdf

# Same silniki w tle + runner lokalnie (silniki dostępne na localhost:8101-8104)
docker compose up -d --build ocrmypdf paddleocr doctr docling
dotnet run --project src/OcrDemo.Runner -- --input D:\skany --output D:\wyniki --engines paddleocr
```

### Visual Studio (2022 17.13+ / 18)

Solucja zawiera projekt `docker-compose.dcproj`. Ustaw go jako projekt startowy i wybierz profil
uruchomienia (`launchSettings.json` obok `.dcproj`):

- **Silniki + runner (debug)** – F5 buduje i startuje wszystkie kontenery, runner startuje po healthcheckach
  silników (`DependencyAwareStart`) z podpiętym debuggerem, przetwarza `input_documents/` i kończy działanie.
- **Tylko silniki** – same kontenery; runner można wtedy debugować lokalnie (`OcrDemo.Runner` jako projekt
  startowy, silniki na `localhost:8101-8104`).

VS używa osobnej nazwy projektu Compose (`ocr-demo-vs`), więc nie zatrzymuje stosu uruchomionego z CLI
(`ocr-demo`) – ale oba korzystają z portów 8101-8104, więc przed F5 wykonaj `docker compose down`.
W trybie Debug (Fast) VS buduje tylko etap `base` Dockerfile runnera i montuje wynik kompilacji z hosta.

Wyniki: `output_documents/<rrrrMMdd-HHmmss>/`

```
report.md                      # podsumowanie + tabela per dokument
report.csv                     # to samo do arkusza (separator ;)
faktura.pdf/                   # katalog per dokument
  ocrmypdf.pdf                 # searchable PDF
  ocrmypdf.txt                 # tekst
  ocrmypdf.json                # strony, linie, słowa, ramki (0..1), tabele
  doctr.tables.html            # podgląd rozpoznanych tabel
  doctr.table-1.csv            # tabela jako CSV (; + BOM – otwiera się w polskim Excelu)
  docling-easyocr.raw.json     # surowa odpowiedź silnika (diagnostyka)
  paddleocr.error.txt          # jeśli silnik zwrócił błąd
```

## Jak liczona jest jakość

Wzorzec `faktura.gt.txt` leży obok `faktura.pdf` (UTF-8). Teksty są normalizowane (NFC, białe znaki →
pojedyncza spacja), następnie:

- **CER** – odległość Levenshteina na znakach / liczba znaków wzorca,
- **WER** – to samo na słowach,
- **Pokrycie słów** – odsetek słów wzorca obecnych w wyniku niezależnie od kolejności.

CER/WER zależą od kolejności czytania (np. tabela odczytana kolumnami zamiast wierszami daje wysoki
CER mimo poprawnie rozpoznanych słów) – dlatego raport pokazuje też pokrycie słów.

## Pomiar czasu

- Czas = czas przetwarzania zmierzony **w kontenerze** (nagłówek `X-Processing-Time-Ms`,
  dla Docling pole `processing_time`), bez przesyłu HTTP.
- Każdy kontener ma ten sam limit CPU i liczbę wątków: `OCR_CPUS` (domyślnie 4, plik `.env`).
- Przed pomiarami runner wykonuje rozgrzewkę (1. strona pierwszego dokumentu, bez pomiaru) –
  wyłączenie: `Runner__Warmup=false`.
- Dokumenty i silniki przetwarzane są sekwencyjnie.

## Architektura

```
src/
  OcrDemo.Core       model wspólny (strony → linie → słowa, tabele; ramki 0..1),
                     IOcrEngine, wczytywanie wejścia (obraz → PDF), zapis warstwy tekstowej, CER/WER
  OcrDemo.Engines    klienci HTTP silników (OCRmyPDF, kontrakt JSON Paddle/docTR, Docling Serve)
  OcrDemo.Runner     konsola: katalog → wszystkie silniki → wyniki + raport
tests/OcrDemo.Tests  testy jednostkowe (warstwa tekstowa, metryki, mapowanie Docling)
sidecars/            kontenery Python: wspólny serwer HTTP (stdlib) + adaptery silników
docker-compose.yml   silniki + runner (jednorazowy przebieg)
```

Przepływ dla jednego dokumentu (docelowo: jedna wiadomość z RabbitMQ):

1. `InputDocumentLoader` – oryginał + **bazowy PDF** (dla obrazów: strony z obrazami w oryginalnej
   rozdzielczości, rozmiar strony z DPI pliku, zastępczo 300 DPI).
2. `IOcrEngine.RecognizeAsync` – silnik dostaje oryginał (PDF rasteryzuje sam w 300 DPI);
   OCRmyPDF dostaje bazowy PDF.
3. `SearchablePdfWriter` – niewidoczny tekst (tryb renderowania 3, czcionka TrueType z polskimi znakami)
   na kopii bazowego PDF; OCRmyPDF zwraca własny PDF.

Kontrakt kontenerów Python (`sidecars/common/ocr_http.py`): `GET /health`,
`POST /ocr?filename=…` (body = surowy plik) → JSON
`{engine, version, pages:[{number, width, height, text, lines:[{text, confidence, box:[x0,y0,x1,y1], words:[…]}], tables:[{box, rows, cols, html, cells:[{row, col, rowSpan, colSpan, text, box}]}]}]}`.

Biblioteki .NET: PdfPig 0.1.16 (Apache-2.0, odczyt/zapis PDF), Magick.NET 15.1 (Apache-2.0, obrazy/TIFF).
PDFsharp nie jest używany – od wersji 6.5 ma licencję AGPL-3.0.

## Konfiguracja

`src/OcrDemo.Runner/appsettings.json`, nadpisywane zmiennymi środowiskowymi (`Engines__Doctr__BaseUrl`)
lub argumentami (`--Runner:Warmup=false`).

| Klucz | Domyślnie | Opis |
|---|---|---|
| `Runner:InputDirectory` / `--input` | `input_documents` | katalog z dokumentami |
| `Runner:OutputDirectory` / `--output` | `output_documents` | katalog wyników |
| `Runner:EngineFilter` / `--engines` | (wszystkie) | lista silników po przecinku |
| `Runner:FallbackImageDpi` | 300 | DPI dla obrazów bez zapisanej rozdzielczości |
| `Runner:FontPath` | DejaVu Sans / Arial | czcionka TTF warstwy tekstowej |
| `Engines:<Silnik>:Enabled` / `BaseUrl` / `TimeoutSeconds` | | per silnik |
| `Engines:Docling:OcrPresets` | `rapidocr, easyocr` | silniki OCR w Docling (każdy = osobna pozycja) |
| `Engines:Docling:OcrLanguages` | `pl, en` | języki OCR (BCP-47) |

Zmienne kontenerów: patrz `docker-compose.yml` (np. `OCRMYPDF_EXTRA_ARGS`, `PADDLE_TEXTLINE_ORIENTATION`,
`DOCTR_DETECT_TABLES`, `PDF_DPI`).

## Ustalenia i ograniczenia

- **PaddleOCR**: z włączonym MKL-DNN (oneDNN) PaddlePaddle 3.3.1 kończy model układu strony błędem
  `ConvertPirAttribute2RuntimeAttribute not support`, dlatego MKL-DNN jest wyłączony – CPU jest przez to
  wyraźnie wolniejsze (~30–40 s/stronę A4 przy 4 CPU). `PPStructureV3(lang="pl")` wybrałby starszy model
  `latin_PP-OCRv5_mobile_rec`; używamy jawnie PP-OCRv6 (obsługuje polski). Obracanie/prostowanie strony
  jest wyłączone, aby współrzędne pasowały do oryginału.
- **docTR**: wbudowane modele rozpoznawania mają słownik „french” (bez ą, ę, ł, ś, ż, ź, ć, ń) – używamy
  modelu `Felix92/doctr-torch-parseq-multilingual-v1` (Apache-2.0, zawiera polskie znaki). Oficjalny obraz
  GHCR bazuje na CUDA, dlatego budujemy własny obraz CPU.
- **Docling**: w obrazie `docling-serve-cpu` jest też Tesseract 4.1.1 tylko z językiem angielskim – nie
  jest porównywany (Tesseract 5.5.4 jest w `ocrmypdf`). Synchroniczny endpoint czeka do `MAX_SYNC_WAIT`
  (podniesione do 3600 s).
- **Warstwa tekstowa** rysowana jest liniami (nie pojedynczymi słowami) – przy rysowaniu słowo po słowie
  pdfium/Chrome sklejał słowa przy kopiowaniu tekstu.
- Strony PDF z atrybutem `/Rotate` ≠ 0 nie są obsługiwane przez generowaną warstwę tekstową.
- Kolejność czytania = kolejność silnika; dla OCRmyPDF tekst pochodzi z `--sidecar` Tesseracta.
