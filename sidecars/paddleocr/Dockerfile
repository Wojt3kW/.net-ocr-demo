# PaddleOCR 3.7 (PP-StructureV3) – CPU.
# Kontekst budowania: ./sidecars
FROM python:3.12-slim

ARG PADDLEPADDLE_VERSION=3.3.1
ARG PADDLEOCR_VERSION=3.7.0
ARG PADDLEX_VERSION=3.7.2

RUN apt-get update \
 && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 libgomp1 curl \
 && rm -rf /var/lib/apt/lists/*

RUN pip install --no-cache-dir \
      "paddlepaddle==${PADDLEPADDLE_VERSION}" \
      "paddleocr[doc-parser]==${PADDLEOCR_VERSION}" \
      "paddlex==${PADDLEX_VERSION}" \
      pypdfium2 pillow numpy

ENV PYTHONUNBUFFERED=1 \
    PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK=True \
    PDF_DPI=300

COPY common /srv/common
COPY paddleocr/app.py /srv/app.py
WORKDIR /srv

# Pobranie modeli w trakcie budowania (kontener działa potem bez internetu, czasy nie zawierają pobierania).
RUN python /srv/app.py --warmup

EXPOSE 8000
HEALTHCHECK --interval=10s --timeout=5s --start-period=120s --retries=30 \
  CMD curl -fs http://127.0.0.1:8000/health || exit 1
ENTRYPOINT ["python", "/srv/app.py"]
