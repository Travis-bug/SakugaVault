# Playwright's official image ships Chromium + all system libraries it needs,
# so we don't fight apt dependencies. The pinned playwright pip version in
# requirements.txt must match this image tag (browsers are baked in for exactly
# that version).
FROM mcr.microsoft.com/playwright/python:v1.48.0-jammy

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

ENV PORT=3400
ENV PYTHONUNBUFFERED=1
EXPOSE 3400

CMD ["python", "-m", "app.server"]
