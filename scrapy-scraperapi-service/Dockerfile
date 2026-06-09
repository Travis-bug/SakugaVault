FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

ENV PORT=3300
ENV PYTHONUNBUFFERED=1
EXPOSE 3300

CMD ["python", "-m", "app.server"]
