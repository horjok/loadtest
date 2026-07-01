# LoadTest

Dağıtık yük testi platformu. HTTP endpoint'lerine paralel istek atar, sonuçları InfluxDB'ye yazar, Grafana'da gösterir.

## Mimari

```
HTTP POST /start-test
        │
        ▼
[Orchestrator - ASP.NET Core]
        │ Redis StreamAddAsync
        ▼
[Redis Stream - Consumer Group]
        │ StreamReadGroupAsync
        ▼
[Worker - Paralel HTTP İstekleri]
        │ WritePointAsync
        ▼
[InfluxDB] ──► [Grafana]
```

Orchestrator, stream'deki bekleyen mesaj sayısına göre Worker container'larını otomatik olarak başlatıp durdurur.

## Stack

- **Orchestrator:** ASP.NET Core 9, Docker.DotNet, StackExchange.Redis
- **Worker:** .NET 9 Console App, StackExchange.Redis, InfluxDB.Client
- **Altyapı:** Redis Streams, InfluxDB 2, Grafana, Docker Compose

## Kurulum

### Gereksinimler

- Docker Desktop
- .NET 9 SDK (lokal geliştirme için)

### Adımlar

```bash
git clone https://github.com/horjok/loadtest.git
cd loadtest

# .env dosyasını oluştur
cp .env.example .env
```

`.env` dosyasını aç ve InfluxDB bilgilerini gir:

```env
INFLUX_URL=http://influxdb:8086
INFLUX_TOKEN=your_token_here
INFLUX_ORG=your_org_here
INFLUX_BUCKET=your_bucket_here
```

```bash
# Tüm servisleri ayağa kaldır
docker compose up --build
```

### InfluxDB Kurulumu

İlk çalıştırmada `http://localhost:8086` adresine gidip kayıt ol. Organization ve bucket adlarını `.env` dosyasıyla eşleştir. API token oluşturduktan sonra `INFLUX_TOKEN` değerini güncelle.

### Grafana

`http://localhost:3000` adresine git. Varsayılan şifre `admin123`.

Data source olarak InfluxDB ekle:
- Query Language: Flux
- URL: `http://influxdb:8086`
- Organization, Token, Default Bucket: `.env` dosyasındakilerle aynı

## Kullanım

### Yük Testi Başlat

```bash
curl -X POST http://localhost:5000/start-test \
  -H "Content-Type: application/json" \
  -d '{"url": "https://example.com", "requests": 100, "duration": 30}'
```

| Alan | Açıklama |
|------|----------|
| `url` | Hedef URL |
| `requests` | Toplam paralel istek sayısı |
| `duration` | Test süresi (saniye) |

### Auto-Scaling Durumu

```bash
curl http://localhost:5000/scale
```

```json
{
  "pendingMessages": 8,
  "currentWorkers": 1,
  "targetWorkers": 2
}
```

Stream'de 5'ten fazla bekleyen mesaj biriktiğinde Orchestrator yeni Worker başlatır. Mesajlar bitince Worker durdurulur. Minimum 1, maksimum 5 Worker.

## Grafana Dashboard

Worker her HTTP isteğinin sonucunu `http_requests` measurement'ına yazar:

- `response_time_ms` — her isteğin süresi (ms)
- `status_code` — HTTP durum kodu (hata durumunda 0)
- `url` — hedef adres (tag)

Flux sorgusu:

```flux
from(bucket: "loadtest")
  |> range(start: -1h)
  |> filter(fn: (r) => r._measurement == "http_requests")
  |> filter(fn: (r) => r._field == "response_time_ms")
```

## Roadmap

- [ ] AWS ECS Fargate'e taşıma
- [ ] P95/P99 latency metrikleri
- [ ] OpenTelemetry distributed tracing
- [ ] Chaos engineering katmanı
- [ ] CI/CD pipeline
- [ ] Unit ve integration testler
