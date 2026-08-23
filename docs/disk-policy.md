# Disk politikası

- Kaynak kodun tek kalıcı konumu `E:\github\FitMatrix` dizinidir.
- Yeni SDK, runtime ve global geliştirme aracı mümkün olduğunda `D:\DevTools` veya mevcut `D:\Tools` altında tutulur.
- npm, NuGet, .NET CLI ve geçici dosya önbellekleri `D:\DevCaches` altında tutulur.
- Projeye özel `node_modules`, `dist`, `bin` ve `obj` çıktıları E: üzerindeki proje ağacında kalır.
- C: sürücüsündeki mevcut .NET SDK ve Git tekrar kurulmaz; mevcut kurulumlar kullanılır.
- Bir araç yalnızca C: üzerine kurulabiliyorsa ve iş için zorunluysa, kurulumdan önce durum açıkça bildirilir.
