# OCRmyPDF (oficjalny obraz) + Tesseract w najnowszej wersji zbudowany ze źródeł
# + modele językowe pol/eng z tessdata_best.
# Kontekst budowania: ./sidecars

ARG OCRMYPDF_VERSION=v17.14.2
# Obraz OCRmyPDF bazuje na Alpine 3.24 – etap budowania musi używać tej samej wersji (ABI leptonica/libstdc++).
ARG ALPINE_VERSION=3.24

FROM alpine:${ALPINE_VERSION} AS tesseract-build
ARG TESSERACT_VERSION=5.5.4
RUN apk add --no-cache build-base autoconf automake libtool pkgconf leptonica-dev curl
WORKDIR /build
RUN curl -fsSL "https://github.com/tesseract-ocr/tesseract/archive/refs/tags/${TESSERACT_VERSION}.tar.gz" | tar xz \
 && cd "tesseract-${TESSERACT_VERSION}" \
 && ./autogen.sh \
 && ./configure --prefix=/opt/tesseract --disable-graphics --disable-doc --without-curl --without-archive \
 && make -j"$(nproc)" \
 && make install \
 && /opt/tesseract/bin/tesseract --version

FROM alpine:${ALPINE_VERSION} AS tessdata
# tessdata_best: najdokładniejsze modele LSTM (wolniejsze od tessdata_fast).
ARG TESSDATA_REPO=tessdata_best
RUN apk add --no-cache curl && mkdir -p /tessdata \
 && for lang in pol eng; do \
      curl -fsSL -o "/tessdata/${lang}.traineddata" "https://github.com/tesseract-ocr/${TESSDATA_REPO}/raw/main/${lang}.traineddata"; \
    done

FROM jbarlow83/ocrmypdf-alpine:${OCRMYPDF_VERSION}
USER root
COPY --from=tesseract-build /opt/tesseract /opt/tesseract
# Modele trafiają obok plików konfiguracyjnych Tesseracta (configs/hocr, configs/txt, ...).
COPY --from=tessdata /tessdata/ /opt/tesseract/share/tessdata/
# osd (wykrywanie orientacji strony) – z pakietu Alpine, bo tessdata_best go nie zawiera
RUN cp /usr/share/tessdata/osd.traineddata /opt/tesseract/share/tessdata/ \
 && ls /opt/tesseract/share/tessdata/configs/hocr
ENV PATH=/opt/tesseract/bin:$PATH \
    TESSDATA_PREFIX=/opt/tesseract/share/tessdata \
    OCR_LANGUAGES=pol+eng \
    PYTHONUNBUFFERED=1
COPY common /srv/common
COPY ocrmypdf/app.py /srv/app.py
RUN tesseract --version && tesseract --list-langs
USER app
WORKDIR /srv
EXPOSE 8000
HEALTHCHECK --interval=10s --timeout=5s --retries=30 CMD python3 -c "import urllib.request;urllib.request.urlopen('http://127.0.0.1:8000/health')"
ENTRYPOINT ["python3", "/srv/app.py"]
