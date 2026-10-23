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

## Ölçekleme

SQL Server ana katalog ve proje verisi için uygundur. Katalog endpoint'i kararlı sıralama ve `offset`/`limit` sayfalaması sunar; istemci bütün sayfaları yükler ve arayüz kategori/metin filtresi uygular. Arama çok daha büyük hacme ulaştığında SQL Server full-text search veya ayrı bir arama indeksi eklenebilir. Görseller ve 3D modeller veritabanına binary olarak gömülmez; dosya/nesne depolamasında tutulup veritabanında URL, lisans ve checksum saklanır.
