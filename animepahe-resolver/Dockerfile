FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

ENV PORT=3400
ENV PYTHONUNBUFFERED=1
EXPOSE 3400

CMD ["python", "-m", "app.server"]
