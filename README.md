# WindSurf SpeechToText (TR)

.NET 8 tabanlı, Türkçe konuşmayı metne çeviren konsol uygulaması. İki mod destekler:

- Azure Speech Service (çevrimiçi)
- Vosk (çevrimdışı)

## Gereksinimler
- .NET 8 SDK
- Mikrofon erişimi
- (Çevrimdışı) Vosk Türkçe modeli: `vosk-model-small-tr-0.3` veya tam model

## Kurulum
Depoyu klonla veya mevcut klasörde çalıştır:
```powershell
# Proje kökünde
dotnet restore
```

## Çalıştırma
### 1) Vosk (çevrimdışı)
Seçenek A: Modeli proje içine kopyala
```powershell
# Proje kökü: spcetotext
cd .\WindSurf_SpeechToText
mkdir .\models\tr -Force
# Model klasörünün İÇERİĞİNİ models/tr içine koyun (iç içe klasör olmasın)
```
Ardından:
```powershell
dotnet run
```

Seçenek B: Ortam değişkeni ile (model projeden ayrıysa)
```powershell
$env:VOSK_MODEL = "C:\\Users\\<kullanici>\\Downloads\\vosk-model-small-tr-0.3\\vosk-model-small-tr-0.3"
dotnet run --project .\WindSurf_SpeechToText\WindSurf_SpeechToText.csproj
```

### 2) Azure (çevrimiçi)
```powershell
$env:AZURE_SPEECH_KEY = "<YOUR_KEY>"
$env:AZURE_SPEECH_REGION = "westeurope"  # bölgenize göre değiştirin
dotnet run --project .\WindSurf_SpeechToText\WindSurf_SpeechToText.csproj
```

## Kullanım
- Uygulama açılınca Vosk modunda mikrofon cihazlarını listeler, bir index seçebilirsiniz (Enter=varsayılan).
- Dinleme kontrolü:
  - Enter: Aç/Kapat
  - S: Oturumu `logs/` altına kaydeder

## Loglar
- `recognized_log.txt`: Nihai ve kısmi tanıma çıktıları
- `error_log.txt`: Hata kayıtları
- `logs/session-*.txt`: Oturum kayıtları

## Notlar
- Vosk için klasör yapısı doğru olmalı: `models/tr` içinde `final.mdl`, `mfcc.conf`, `HCLr.fst`, `Gr.fst`, `ivector/` vb.
- NETSDK1206 bir uyarıdır; çalışmayı engellemez.
- Daha iyi doğruluk için "small" yerine tam Türkçe model önerilir.

## Geliştirme
```powershell
# Build
 dotnet build .\WindSurf_SpeechToText\WindSurf_SpeechToText.csproj -c Debug
# Çalıştır
 dotnet run --project .\WindSurf_SpeechToText\WindSurf_SpeechToText.csproj
```

## Lisans
Bu proje için lisans belirtilmemiştir. Eklenmesi önerilir.
