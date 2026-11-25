# docTR 1.2.0 (PyTorch, wersja CPU).
# Oficjalny obraz ghcr.io/mindee/doctr bazuje na nvidia/cuda – tu budujemy lżejszy obraz tylko na CPU.
# Kontekst budowania: ./sidecars
FROM python:3.12-slim

ARG DOCTR_VERSION=1.2.0

RUN apt-get update \
 && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 libgomp1 \
 && rm -rf /var/lib/apt/lists/*

RUN pip install --no-cache-dir torch torchvision --index-url https://download.pytorch.org/whl/cpu \
 && pip install --no-cache-dir "python-doctr==${DOCTR_VERSION}" pypdfium2 pillow numpy

ENV PYTHONUNBUFFERED=1 \
    PDF_DPI=300 \
    HF_HUB_DISABLE_TELEMETRY=1

COPY common /srv/common
COPY doctr/app.py /srv/app.py
WORKDIR /srv

# Pobranie modeli w trakcie budowania (kontener działa potem bez internetu, czasy nie zawierają pobierania).
RUN python /srv/app.py --warmup

EXPOSE 8000
HEALTHCHECK --interval=10s --timeout=5s --start-period=120s --retries=30 \
  CMD python -c "import urllib.request;urllib.request.urlopen('http://127.0.0.1:8000/health')"
ENTRYPOINT ["python", "/srv/app.py"]
