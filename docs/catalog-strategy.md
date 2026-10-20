# Ürün kataloğu stratejisi

## Hedef

FitMatrix kataloğu yalnızca örnek 3D nesneleri değil, gerçek üretici ürünlerini de saklayabilecek şekilde tasarlanmıştır. Mutlak anlamda "dünyadaki bütün ekipmanlar" tek bir kaynaktan alınamaz; doğru yaklaşım birden fazla kaynağı ortak şemaya dönüştüren, doğrulayan ve güncelleyen bir katalog hattıdır.

## Kimlik ve tekrarları önleme

Ürünler mümkün olduğunda şu anahtarlarla eşleştirilir:

1. GTIN / EAN / UPC
2. Üretici + MPN (Manufacturer Part Number)
3. Üretici + normalize model adı

Aynı modelin renk, bölge veya revizyon farkları ayrı varyantlar olarak tutulabilir.

## Ölçü modeli

Her üründe en az şu fiziksel alanlar bulunur:

- Gövde genişliği, yüksekliği ve derinliği
- Masa ile temas eden footprint genişliği ve derinliği
- Varsa ağırlık
- Ölçünün normalize edildiği birim

Paket ölçüsü ile ürün ölçüsü birbirine karıştırılmaz. Monitörlerde panel genişliği ile stand tabanı; monitör kollarında kelepçe alanı ile kolun erişim hacmi; bilgisayar kasalarında dış gövde ile gerekli havalandırma boşluğu ayrı kavramlardır.

Veritabanı santimetreyi kanonik fiziksel birim olarak kullanır. Kaynak birimi ve ham değer, içe aktarma/audit verisinde ayrıca korunabilir.

## Kategoriye özel özellikler

Ortak ürün tablosuna yüzlerce boş kolon eklemek yerine `AttributeDefinitions` ve `ProductAttributeValues` tabloları kullanılır.

Örnekler:

- Monitör: diyagonal, panel türü, yenileme hızı, VESA düzeni
- Kasa: form faktörü, maksimum GPU uzunluğu, radyatör desteği
- Klavye: düzen, switch türü, bağlantı biçimi
- Monitör kolu: taşıma kapasitesi, erişim, kelepçe aralığı

## Kaynak önceliği

1. Üreticinin resmi ürün sayfası veya teknik föyü
2. Lisanslı/izinli yapılandırılmış katalog sağlayıcısı
3. Yetkili satıcı verisi
4. Topluluk katkısı — doğrulanmamış olarak işaretlenir

Her ürünün `ProductSources` kaydı sağlayıcıyı, kaynak URL'sini, harici kimliği, lisansı ve son kontrol tarihini tutar. Kaynak veremediğimiz kesin bir ölçü yayımlanmaz.

## Olası veri sağlayıcıları

- UPCitemdb ücretsiz deneme uç noktası: hesap açmadan ürün adı/model/GTIN kimliği ve sağlayıcının yayımladığı temel alanlar. Kota düşük olduğu için SQL önbelleği ve yalnızca belirli ürün sorguları kullanılır.
- Üreticinin resmî ürün sayfasındaki Schema.org/Product JSON-LD, `additionalProperty`, HTML teknik tabloları, tanım listeleri ve özellik kartları: adlandırılmış ölçüler ile yayımlanan diğer teknik alanlar için birincil zenginleştirme kaynağı.
- Hesap gerektirmeyen Brave Search ve DuckDuckGo HTML keşfi: yalnızca marka alan adı veya izinli teknik kaynak havuzundaki sonuçlar değerlendirilir; kaynak URL her alanla birlikte korunur.
- Modelden türetilen doğrudan kaynak yolları: arama servisi kota uygulasa dahi AOC monitörleri ile desteklenen fare/klavye teknik katalogları sorgulanabilir.
- Open Icecat: marka tarafından yetkilendirilmiş açık ürün içeriği; daha geniş kapsam için ücretli Full Icecat.
- GS1/GDSN: ürün kimliği ve standartlaştırılmış temel veri alışverişi için güçlü standart; erişim ve kapsam ticari koşullara bağlıdır.
- Üreticilerin resmi API, ürün feed'i veya teknik dokümanları.

Başka karşılaştırma sitelerinin arayüzünü veya veritabanını izinsiz kopyalamak veri kaynağı stratejisi değildir. Sağlayıcı lisansı, robots kuralları, telif hakları ve kullanım şartları incelenmeden otomatik toplama yapılmaz.

## İçe aktarma hattı

```text
Sağlayıcı → ham staging → kimlik eşleştirme → birim normalizasyonu
          → kategori doğrulama → kaynak/provenance → yayın kataloğu
```

Bir ürün aşağıdaki kalite durumlarından birini taşır:

- `template`: editörün jenerik başlangıç nesnesi
- `imported`: kaynağı kayıtlı ancak henüz doğrulanmamış içe aktarım
- `source-verified`: üretici veya lisanslı sağlayıcı kaynağı doğrulanmış kayıt

Çalışan ilk içe aktarma yüzeyi `POST /api/admin/catalog/import` adresindedir ve güvenlik nedeniyle yalnızca Development ortamında açılır. İstekler GTIN'i, yoksa marka + MPN birleşimini kullanarak idempotent upsert yapar. Kategori, pozitif ölçü, HTTP/HTTPS kaynak adresi ve kategoriye özel özellik türleri sunucu tarafında doğrulanır. Örnek istek `docs/samples/catalog-import.example.json` dosyasındadır.

## Ürün adıyla ölçü bulma

Yerel başlangıç kataloğu, jenerik editör nesnelerine ek olarak 26 üreticiden 43 popüler gerçek modeli içerir. Bu kayıtlar klavye, mouse, monitör, bilgisayar kasası ve masa gruplarını kapsar; üretici kaynak bağlantısı taşır ve kullanıcı tarafından silinirse her açılışta zorla geri eklenmez. Açık web aramasındaki üretici dizini 70'ten fazla markayı ve çok kelimeli marka adlarını resmî alan adlarıyla eşler.

`GET /api/product-discovery/search?query=...` yerel SQL kataloğunda ürün adı, marka, model, MPN ve GTIN alanlarını birlikte arar. Sonuçlar gövde genişliği/yüksekliği/derinliği, footprint, ağırlık, veri kalitesi ve kaynak sayısıyla döner.

Çok kelimeli sorgular tek bir kolonda bitişik olmak zorunda değildir. Örneğin `Dell U2723QE`, marka alanındaki `Dell` ile model/MPN alanındaki `U2723QE` değerini birlikte eşleştirir. Doğrulanmış kesin yerel eşleşme bulunduğunda dış sağlayıcı çağrısı atlanır.

Belirli ürün sorguları hesap gerektirmeyen UPCitemdb aramasına gider. Sonuç yetersizse önce desteklenen doğrudan üretici/teknik yolları, ardından Bing, Google, Brave, DuckDuckGo ve Mojeek üzerinden açık web keşfi denenir. Marka ile eşleşen resmî üretici alan adı en yüksek önceliği alır; diğer sonuçlar yalnızca güvenilir teknik kaynak havuzundaysa okunur. HTML tablo, `dt/dd`, özellik kartı, JSON-LD `PropertyValue`, PDF metni ve W×H×D ifadeleri ortak alan modeline dönüştürülür. Sonuçlar 14 gün, sonuçsuz aramalar 5 dakika SQL'de önbelleğe alınır; doğrulanmış yerel kayıtlar 90 gün sonra yeniden zenginleştirilmeye uygun sayılır. Tamamlanmamış veya tek satırlık sırası belirsiz ölçüler açık bir güven durumu taşır.

Open Icecat kimlik bilgileri ortam değişkenleriyle zaten tanımlıysa yalnızca hesap gerektirmeyen arama sonuç vermediğinde isteğe bağlı yedek olarak çalışır. Dış kaynaktan gelen bütün teknik özellikler korunur ve standart birimler santimetre/kilograma çevrilir. Paket/kargo ölçüleri ürün gövde ölçüsü olarak kullanılmaz. Monitör standı gibi fiziksel temas alanı ayrıca sağlanmıyorsa otomatik tahmin edilmez; kullanıcı onayı veya üretici doğrulaması beklenir.

Gövde ölçüleri ve kaynak URL'si bulunan bir sonuç, `POST /api/catalog/products/from-discovery` ile yayın kataloğuna alınabilir. Önce gerçek santimetre footprint'i masada 0° ve 90° yönlerde taranır; sınır ve SAT çakışma denetiminden geçen merkeze en yakın boş konum seçilir. Boş alan yoksa ürün kataloğa kaydedilebilir ancak sahneye hatalı biçimde bindirilmez. Masa kategorisindeki sonuçlar ekipman gibi masanın üstüne konmaz; projenin çalışma alanı ve yüksekliği olarak uygulanır. Resmî yapılandırılmış ölçü kaydı `source-verified`, bağımsız teknik kaynak ise kaynak türü açıkça belirtilen veri olarak saklanır. Esnek teknik özellikler `ProductSpecifications` tablosunda grup, ad, değer, birim, kaynak URL'si ve son kontrol zamanı ile korunur; ürün veri sayfası bunları kaynak gruplarına göre gösterir.

## Üretici ürün haritalarından toplama

Kataloğu en hızlı büyüten yol, üreticilerin arama motorları için yayımladığı `sitemap.xml` dosyalarıdır. `backend/Data/Harvest/manufacturers.json` her üretici için haritaların adresini, hangi adreslerin masa ekipmanı ürün sayfası olduğunu (`include`), varyantların nasıl tekilleştirileceğini (`dedupePathSegments`), teknik sayfanın ürün adresine göre konumunu (`pageSuffix`, örneğin ASUS `techspec/`) ve adres kalıbından kategori ipuçlarını tutar. Şu an Logitech, Logitech G, Razer, SteelSeries ve ASUS tanımlıdır.

İş arama motoruna hiç uğramaz: haritadaki her ürün sayfası doğrudan okunur, `robots.txt` içindeki `User-agent: *` yasakları uygulanır ve sayfalar arasında varsayılan 3 saniye beklenir. Ölçüler sayfadaki yapılandırılmış veriden (JSON-LD, tablolar, gömülü ürün verisi, `Width: 63.9 mm` gibi listeler) okunur; inç ve milimetre birlikte yazıldığında metrik değer, ön/arka gibi birden fazla değer olduğunda en büyüğü alınır.

Kayıttan önce:

- Masaya yatık ürünlerde (klavye, mouse, mousepad, dizüstü, tablet, telefon) en ince kenar yükseklik sayılır; üreticilerin eksen adları birbirini tutmaz.
- Her ürün türü için en uzun kenar sınırı vardır (örneğin kulaklık 40 cm); kablo uzunluğu gibi yanlış okumalar reddedilir.
- Paket/set ürünleri ve sınıflandırılamayan genel aksesuarlar (kablo, alıcı, kılıf) alınmaz.
- Daha önce kaydedilmiş sayfalar ağa çıkmadan atlanır; iş kesilirse yeniden başlatıldığında kaldığı yerden sürer.

### JavaScript ile yüklenen sayfalar

Bazı üreticiler (IKEA, Fractal Design) teknik özellikleri sayfaya tarayıcıda JavaScript ile ekler; düz HTTP isteğiyle gelen HTML'de ölçü yoktur. Bu kaynaklar `manufacturers.json` içinde `"render": true` ile işaretlenir ve Playwright ile başsız tarayıcıda açılır:

- Windows'ta kurulu Microsoft Edge kullanılır (`BrowserRendering:Channel = msedge`); ayrı tarayıcı indirilmez. Diğer sistemlerde `playwright install chromium` ile Chromium kurulduysa o kullanılır, kurulmadıysa bu kaynaklar sessizce atlanır (Docker imajı varsayılan olarak tarayıcı içermez).
- Sayfa yüklendikten sonra "Specifications", "Measurements", "Dimensions/Weights" gibi bölümler açılır; görünür metin de okunur, böylece ayrı öğelere bölünmüş "Width: 105 cm" satırları yakalanır.
- Aynı anda tek sayfa açılır; bot korumasıyla yanıt veren siteler (Dell, Corsair, MSI, Herman Miller) atlanır, koruma aşılmaya çalışılmaz.
- `"mergeColourVariants": true` renk varyantlarını tek ürüne indirir; IKEA için ürün türü adresten (`typeFromUrl`) alınır: "IKEA MICKE desk, 105x50 cm".

Yeni bir üreticiyi eklemeden önce tek bir ürün sayfası Development ortamında denenebilir:

```text
GET /api/admin/catalog/page-probe?url=<ürün adresi>&brand=<marka>&render=true&debug=true
```

Ürün sayfası ölçüyü hiç yayımlamayan ya da yalnızca parça ölçüleri veren ürünler (Razer klavyeleri, Secretlab sandalyeleri) bu yoldan da gelmez; onlar için model listeleri ve kullanıcı aramaları kullanılır.

## Model listeleriyle toplu toplama

Kullanıcı aramalarını beklemeden kataloğu binlerce ürüne çıkarmak için arka planda çalışan bir toplama işi vardır. İş, `backend/Data/Harvest/*.txt` dosyalarındaki gerçek marka + model adlarını (monitör, klavye, mouse, ses, bilgisayar, mobilya/aydınlatma, cihazlar) tek tek editörün kullandığı arama hattından geçirir. Listeler yalnızca ürün kimliği içerir; hiçbir ölçü elle yazılmaz. `[audio:headphone]` gibi bir satır, altındaki modellerin kategori ve 3D türünü belirler; böylece sayfasında "kasa" kelimesi geçmeyen `NZXT H5 Flow` da doğru sınıfa girer.

Bir sonuç yalnızca şu koşullarda kataloğa yazılır:

- Gövde genişliği, yüksekliği ve derinliğinin üçü de yayımlanmış olmalı.
- Ölçünün alındığı sayfanın adresi bulunmalı.
- Varsayılan olarak ölçü güveni `official-structured` veya `source-structured` olmalı; sağlayıcının tek satırlık sırası belirsiz ölçüleri (`provider-order`) ancak açıkça izin verilirse alınır.

Kayıtlar GTIN, yoksa marka + MPN, o da yoksa marka + model adıyla tekilleştirilir; iş tekrar çalıştırıldığında var olan ürünler güncellenir, kopyalanmaz. Uzak sitelere yük bindirmemek için ağ isteği yapan her sorgudan sonra varsayılan 6 saniye beklenir; art arda 12 hata olursa iş kendini durdurur. Yerel katalogda doğrulanmış karşılığı olan sorgular ağa çıkmadan atlanır.

Uç noktalar yalnızca Development ortamında açıktır:

- `GET /api/admin/catalog/harvest/lists`: kullanılabilir listeler ve sorgu sayıları
- `POST /api/admin/catalog/harvest`: `{ "lists": ["monitors"], "queries": [], "requireVerified": true, "delaySeconds": 6, "maxQueries": 20 }`
- `GET /api/admin/catalog/harvest`: ilerleme ve son 60 sonuç
- `GET /api/admin/catalog/harvest/manufacturers`: tanımlı üretici kaynakları
- `POST /api/admin/catalog/harvest/manufacturers`: `{ "sources": ["logitech", "asus"], "delaySeconds": 3, "maxPagesPerSource": 20 }`
- `DELETE /api/admin/catalog/harvest`: çalışan işi durdurur

Komut satırından `scripts/harvest-catalog.ps1` aynı işi başlatır, durumunu gösterir ve durdurur.

versus.com gibi karşılaştırma sitelerinin veritabanı bu iş için kaynak olarak kullanılmaz; o veriler sitenin kendi emeğidir ve kullanım şartları otomatik kopyalamaya izin vermez. Bunun yerine ürün kimliği listeden, ölçü üreticinin ve güvenilir teknik kaynakların sayfalarından gelir.

## Ölçekleme

SQL Server ana katalog ve proje verisi için uygundur. Katalog endpoint'i kararlı sıralama ve `offset`/`limit` sayfalaması sunar; istemci bütün sayfaları yükler ve arayüz kategori/metin filtresi uygular. Arama çok daha büyük hacme ulaştığında SQL Server full-text search veya ayrı bir arama indeksi eklenebilir. Görseller ve 3D modeller veritabanına binary olarak gömülmez; dosya/nesne depolamasında tutulup veritabanında URL, lisans ve checksum saklanır.
