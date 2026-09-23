using System.Globalization;

namespace Crm.Cli.Reports;

/// <summary>
/// The "Nasıl okunur" sheet each workbook opens with. What used to be a Markdown file beside the table now travels
/// inside it: a reader who opens one file has the columns, the caveats and the counts in front of them, and there
/// is no second document to keep in step with the first.
///
/// <para>
/// Every column is listed with the decision it serves. That is also the test a column has to pass to exist: if a
/// line cannot be written here saying what a reader does differently because of it, the column comes out.
/// </para>
/// </summary>
public static class Guides
{
    public static Sheet Plan(int workflows, int live, int excluded, int buildingBlocks)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar",
            "Taşınacak işin listesidir: her iş akışı için bir satır. Yalnızca sizin kurmanız gerekenler buradadır, "
            + "süzmenize gerek yok. Plandan çıkarılanlar ve gerekçeleri Kurumsal Mimari'de ayrı bir dosyadadır.");
        sheet.Row("Sayfa sırası",
            "Taşıma planı (ana liste) → Çağrı ağacı (bir akış tek başına mı) → Süreç ağaçları (işi kalemlere böl) → "
            + "Sapma (çalışan kopya çizimden farklı) → Okunamayan yapılar (çizimin eksik yeri). Kılavuz da bu sırayı izler.");
        sheet.Row("Sayfaların boş olması",
            "Çağrı ağacı ve Süreç ağaçları YALNIZCA birbirini çağıran akışları taşır; boşsa hiçbir akış başkasını çağırmıyor "
            + "demektir. Sapma boşsa iyi haberdir: CRM'de çalışan kopyalar tanımlarıyla aynı. Okunamayan yapılar boşsa "
            + "bütün diyagramlar tamdır.");
        sheet.Row("is_akisi · kategori · birincil_varlik", "Ne olduğu ve hangi kayıt türü üzerinde çalıştığı. İşi varlığa göre bölerken bu sütunu kullanın.");
        sheet.Row("tetikleyici", "Akışı ne başlatır. İlk tasarım kararı budur: yeni üründe aynı olayın karşılığı var mı, yoksa olayı siz mi üreteceksiniz?");
        sheet.Row("adim", "Akışın büyüklüğü. Tahmin için; sayı büyüdükçe kalem büyür.");
        sheet.Row("bekleme_var", "evet ise akış bir zamanlayıcı ya da bir koşul bekliyor: süreç saatlerce, günlerce açık kalır. "
            + "Zamana yayılan bir süreç, baştan sona koşan bir süreçle aynı şey değildir; tasarımı buna göre kurun.");
        sheet.Row("rol", "\"giriş noktası\" bir bütün olarak taşınır. \"yapı taşı\" başka akışlarca paylaşılır: tek başına taşımayın, bir kez taşıyın.");
        sheet.Row("aile · aile_rolu", "Bu akışa çok benzeyen başkaları varsa ailenin adı (ailenin başlangıç noktası olan akış). "
            + "Dolu ise tasarıma tek tek değil, aile olarak başlayın — ayrıntısı aileler.xlsx.");
        sheet.Row("kullanim · son_kayitli_calisma",
            "Canlı mı: \"çalışıyor · tarih\", \"kayıtlı çalışma yok\" ya da \"bilinemez\". Tarihe göre sıralayabilirsiniz. "
            + "DİKKAT: kaydın bulunmaması kullanılmadığını KANITLAMAZ — CRM sistem işlerini düzenli olarak siler, iş kuralları "
            + "hiç iz bırakmaz, gerçek zamanlı akışlar yalnızca hatayı kaydeder. \"kayıtlı çalışma yok\" bir silme gerekçesi değildir.");
        sheet.Row("okunamayan_adim", "Sıfırsa çizim tamdır. Sıfırdan büyükse çizim eksiktir: diyagramda o adımlar OKUNAMADI olarak "
            + "işaretlidir; o akışı bitirmeden CRM'de karşılıklarına bakın. Hangi yapının okunamadığı Okunamayan yapılar sayfasındadır.");
        sheet.Row("ozel_etkinlikler", "Dışarıya uzanan çağrılar. Doluysa ayrı bir entegrasyon kalemi açın — ayrıntısı dis-sistemler.xlsx.");
        sheet.Row("baslattigi_is_akisi · paylasilan_alan",
            "Kaç akışı tetikliyor ve kaç alanı başkalarıyla paylaşıyor. İkisi de sıfırdan büyükse bu akış tek başına tasarlanamaz — veri-analizi.xlsx.");
        sheet.Row("yazdigi_varliklar", "Neye dokunduğu. Yeni tasarımın dış dünyaya verdiği sözdür; alan alan dökümü veri-analizi.xlsx'tedir.");
        sheet.Row("mod", "\"Gerçek zamanlı\" akış kullanıcıyı bekletir, \"arka plan\" bekletmez. Aynı işi arka plana almak davranışı değiştirir.");
        sheet.Row("hassas_deger_var", "XAML içinde adres, kullanıcı adı veya parola benzeri değer var. Değerlerin kendisi kısıtlı rapordadır.");
        sheet.Row("bpmn_dosyasi · birlesik_dosya · is_akisi_id", "Nereye gideceğiniz: akışın diyagramı, ailesinin birleşik modeli ve CRM'de aramak için kimliği.");
        sheet.Row("İş yükü nasıl hesaplanır",
            "Bir üst akış ve çağırdığı alt akışlar TEK bir taşıma kalemidir. Süreç ağaçları sayfası bu kalemleri gösterir; "
            + "kalem sayısı iş akışı sayısından çok daha azdır.");
        sheet.Row("Okunamayan yapılar sayfası", "is_akisi · yapi · kac_kez · bpmn_dosyasi. Aynı yapı bir akışta kaç kez okunamadıysa "
            + "tek satırdır; diyagramı açıp OKUNAMADI kutularını bulun. Aynı yapi birçok akışta geçiyorsa bize bildirin, "
            + "ayrıştırıcıya eklenebilir.");
        sheet.Row("Sapma sayfası", "Çalışan kopyası tanımından GERÇEKTEN farklı olan akışlar. Buradaki her satır için diyagram "
            + "üretimdeki davranışı göstermeyebilir: CRM'de açıp çalışan kopyayı esas alın.");
        sheet.Row("Bu çalıştırma",
            string.Create(CultureInfo.InvariantCulture,
                $"{workflows} taşınacak iş akışı · {live} canlı süreç · başka bir akışça çağrılan {buildingBlocks} · kapsam dışı {excluded}"));
        sheet.Row("Uyarı", "Modeller açıklayıcıdır, çalıştırılabilir değildir. Diyagramlar üretim verisi içerir; kurum dışına çıkarmayın.");
        return sheet;
    }

    public static Sheet Excluded(int excluded, int drafts, int supplied, int all)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar",
            "Taşıma planından ÇIKARILAN iş akışlarıdır. Buradaki hiçbir satır için iş planlamayın; kitap yalnızca "
            + "bir çıkarma kararını sorgulamak istediğinizde açılır.");
        sheet.Row("neden", "Çıkarma gerekçesi. Yalnızca üç kesin gerekçe kullanılır; şüphe varsa akış planda bırakılmıştır.");
        sheet.Row("ürünle gelmiş", "CRM bu akışı yönetilen bir çözümün parçası olarak bildiriyor: ürünle birlikte gelmiş, kurum yazmamış.");
        sheet.Row("taslak", "Tanım taslak durumda; CRM taslak bir tanımla yeni çalıştırma başlatmaz.");
        sheet.Row("adı deneme gibi", "Adı DRAFT/TEST/kopya gibi okunuyor VE kanıt dosyasında hiç kayıtlı çalışması yok. "
            + "Yalnızca ad yeterli değildir: adı deneme gibi görünen ve üretimde çalışan akışlar planda kaldı.");
        sheet.Row("Diyagramı yine de var mı", "Evet. Her akışın BPMN dosyası üretildi; bpmn_dosyasi sütunundadır.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"{all} iş akışının {excluded} tanesi kapsam dışı · {drafts} taslak · {supplied} ürünle gelen"));
        return sheet;
    }

    public static Sheet Families(int families, int combined, int notCombined)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "Hangi iş akışlarının aslında aynı işi yaptığına karar vermenize yarar. "
            + "Tek kararı siz verirsiniz: bu akışlar yeni üründe tek bir süreç mi olacak?");
        sheet.Row("Sayfa sırası", "Aileler (kimler bir arada) → Birleştirme (ailenin tek modeli) → Yakın çiftler (aile olmayan ama benzeyenler)");
        sheet.Row("Nasıl kullanılır",
            "Aileler sayfasında bir aileyi seçin, üyelerin diyagramlarını ve ailenin birleşik modelini yan yana açın, "
            + "aynı işi yapıp yapmadıklarına karar verin ve kararınızı karar sütununa yazın. Araç yalnızca benzerlik ölçer.");
        sheet.Row("aile", "Ailenin adı, ailenin başlangıç noktası olan iş akışıdır: incelemeye o akıştan başlayın.");
        sheet.Row("aile_rolu", "\"başlangıç noktası\" ailenin en merkezdeki üyesi; diğerlerini ona göre karşılaştırın.");
        sheet.Row("baslangica_benzerlik", "Üyenin başlangıç noktasına benzerliği (0–1). Düşük olanlar aileye en zayıf bağlı üyelerdir: önce onları sorgulayın.");
        sheet.Row("zayif_tutarlilik", "evet ise aile gevşek: muhtemelen tek bir süreç değildir, ayırmayı düşünün.");
        sheet.Row("birincil_varlik · kategori", "Farklı varlıklar üzerinde çalışan iki akış nadiren aynı süreçtir. Karar verirken buna bakın.");
        sheet.Row("son_kayitli_calisma", "Hangi üyenin canlı olduğu. Birleştirmede canlı olanı esas alın.");
        sheet.Row("karar", "Sizin doldurmanız için boş bırakıldı: birleşsin / ayrı kalsın / incelenecek.");
        sheet.Row("Birleştirme sayfası", "Ailenin birleşik modeli ve üyeleri. cesitleme_sayisi, üyelerin birbirinden ayrıldığı nokta sayısıdır: "
            + "sıfırsa üyeler aynı işi yapıyor, büyüdükçe birleştirme tartışmalıdır. birlesik_dosya ve uye_bpmn açılacak dosyalardır.");
        sheet.Row("Yakın çiftler sayfası", "Aile OLMAMIŞ ama karara değer çiftler: eşiğe yakın kalanlar ve \"aynı yapı, farklı ad\" olanlar — "
            + "yani birinin kopyalanıp yeniden adlandırılmış olması muhtemel olanlar. Her karşılaştırmanın puanı değil, yalnızca bunlar listelenir.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"2+ üyeli {families} aile · {combined} birleştirildi · {notCombined} birleştirilmedi"));
        return sheet;
    }

    public static Sheet Data(int fields, int sharedFields, int cascades, int pairs)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "Bir akışı tek başına tasarlayamayacağınız yerleri gösterir: aynı veriye dokunan başka akışlar "
            + "ve birbirini kendiliğinden tetikleyen zincirler.");
        sheet.Row("Sayfa sırası", "Veri ayak izi (alan alan: kim yazıyor, kim okuyor) → Tetikleme zincirleri (bir yazmanın başlattığı akışlar)");
        sheet.Row("varlik · alan", "Hangi kaydın hangi alanı. Tasarladığınız akışın yazdığı alanları burada aratın.");
        sheet.Row("yazan", "Bu alana yazan akış sayısı. 1'den büyükse bu alanın sırası CRM'de hiçbir zaman garanti edilmedi: "
            + "yeni üründe bir sıra kararlaştırın ya da yazan akışları birleştirin. Sayfayı bu sütuna göre sıralayarak başlayın.");
        sheet.Row("bu_alanin_baslattigi", "Bu alana yazmak kaç akışı başlatıyor. Sıfırdan büyükse alana dokunmak zincir tetikler.");
        sheet.Row("okuyan", "Alanı okuyan akış sayısı. Alanın anlamını değiştirecekseniz kimin etkileneceğini söyler.");
        sheet.Row("yazanlar · baslattiklari · okuyanlar", "Aynı sayıların adları: kiminle konuşacağınızı burada bulursunuz.");
        sheet.Row("Tetikleme zinciri nedir",
            "İlk akış, ikincinin izlediği bir alana yazdığı ya da bir kayıt oluşturduğu için CRM ikinciyi başlatır. "
            + "Aralarında açık bir çağrı yoktur; bu bağ diyagramlarda GÖRÜNMEZ, yalnızca bu sayfadadır.");
        sheet.Row("kendini_baslatiyor", "evet ise akış kendi yazmasıyla yeniden tetikleniyor: yeni üründe bu döngü elle kırılmalıdır.");
        sheet.Row("baslatan_modu · baslayan_modu", "Zincirin gerçek zamanlı mı arka planda mı koştuğu. Gerçek zamanlı bir başlatıcı, "
            + "zinciri kullanıcının kaydetme anının içine sokar.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"{fields} alan · birden fazla akışın yazdığı {sharedFields} alan · {pairs} çift arasında {cascades} tetikleme zinciri"));
        return sheet;
    }

    public static Sheet External(int activities, int addresses, int hosts)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "İş akışlarının CRM dışına uzanan çağrılarını gösterir: entegrasyon yükü buradadır.");
        sheet.Row("Sayfa sırası", "Dış bağımlılıklar (etkinlik başına: kim çağırıyor) → Adresler (tanımın içinde geçen her adres)");
        sheet.Row("Nasıl çalışır",
            "Bir CRM iş akışı bir servisi kendi başına çağıramaz; tek yol, CRM'e kaydedilmiş özel bir etkinliktir (derlenmiş kod). "
            + "Dışarıya uzanan her çağrı bu yüzden bir satır olarak görünür.");
        sheet.Row("etkinlik · derleme", "Çağrılan kodun adı ve içinde bulunduğu derleme. Yeni üründe her birinin karşılığını kurmanız gerekir; "
            + "ne yaptığını yalnızca derlemenin sahibi söyleyebilir.");
        sheet.Row("cagiran_is_akisi_sayisi · cagiran_is_akislari", "Kaç akışı etkiliyor. Üstteki satırlar en çok akışı etkileyenlerdir: oradan başlayın.");
        sheet.Row("Görülemeyen",
            "Etkinliğin kendi derlemesi içinde ne yaptığı XAML'de yoktur: koda ya da konfigürasyona gömülü bir adres buradan "
            + "GÖRÜNMEZ ve çoğu adres oradadır. Ayrıca eklentiler (plug-in) iş akışı değildir; bu envantere hiç girmezler.");
        sheet.Row("Boş Adresler sayfası", "\"Hiçbir yere bağlanmıyor\" demek DEĞİLDİR: yalnızca hiçbir adresin tanım metninin "
            + "içine yazılmadığı anlamına gelir.");
        sheet.Row("sunucu", "Adresin işaret ettiği sunucu. Sayfayı bu sütuna göre sıralayın: hangi dış sistemlere dokunulduğu böyle görünür.");
        sheet.Row("adres", "Bulunan adresin kendisi. İçine yazılmış kullanıcı adı ve parola maskelenmiştir (***).");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"{activities} özel etkinlik · {hosts} farklı sunucu · {addresses} farklı adres"));
        sheet.Row("Uyarı", "Adresler sayfası üretim adresleri taşır; kurum dışına çıkarmayın.");
        return sheet;
    }

    private static Sheet Empty()
    {
        return new Sheet(SheetNames.Guide, "Konu", "Açıklama");
    }
}
