using System.Globalization;

namespace Crm.Cli.Reports;

/// <summary>
/// The "Nasıl okunur" sheet each workbook opens with. What used to be a Markdown file beside the table now travels
/// inside it: a reader who opens one file has the columns, the caveats and the counts in front of them, and there
/// is no second document to keep in step with the first.
/// </summary>
public static class Guides
{
    public static Sheet Plan(RunState state, int workflows, int live, int drafts, int supplied, int buildingBlocks)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar",
            "Taşınacak işin listesidir. Her iş akışı için bir satır; sıralama ve süzme sizindir.");
        sheet.Row("Sayfalar",
            "Taşıma planı (ana liste) · Kullanım (çalışma kanıtı) · Çağrı ağacı ve Süreç ağaçları (hangi akış hangisini çağırıyor) · "
            + "BPMN dizini (diyagram dosyaları) · Okunamayan yapılar ve Yapı sıklığı (ayrıştırıcının okuyamadıkları) · Sapma (tanım ile çalışan kopya farkı)");
        sheet.Row("Nereden başlanır",
            "Taşıma planı sayfasında öncelik = 1 ile süzün: canlı süreçler. 2 diyalog/iş kuralı/süreç akışı, 3 adı deneme gibi okunanlar, "
            + "4 taslaklar (çalışamaz), 5 ürünle gelenler (sizin kurmanız gerekmez).");
        sheet.Row("İş yükü nasıl hesaplanır",
            "Bir üst akış ve çağırdığı alt akışlar tek bir taşıma kalemidir. Süreç ağaçları sayfası bu kalemleri gösterir; "
            + "kalem sayısı iş akışı sayısından çok daha azdır.");
        sheet.Row("priority / öncelik",
            "1 canlı süreç · 2 diyalog, iş kuralı veya süreç akışı · 3 adı deneme gibi okunuyor · 4 taslak, çalışamaz · 5 ürünle gelmiş");
        sheet.Row("tetikleyici", "Akışı ne başlatır: kayıt oluşturma, adı verilen alanların güncellenmesi, silme, istek üzerine.");
        sheet.Row("adim / okunamayan_adim", "Akışın büyüklüğü ve ayrıştırıcının okuyamadığı adım sayısı. Okunamayanları elle kontrol edin.");
        sheet.Row("ozel_etkinlikler", "Yeni üründe karşılığı bulunmayan iş ortağı veya kurum içi kod. Ayrıntısı dis-sistemler.xlsx kitabındadır.");
        sheet.Row("cagirdigi / cagiran / rol", "Çağrı ağacı: giriş noktası bir bütün olarak taşınır, yapı taşı birden çok süreççe paylaşılır.");
        sheet.Row("aile / aile_rolu / birlesik_dosya", "Birbirine çok benzeyen akışlar ve ailenin birleşik modeli. Ayrıntısı aileler.xlsx kitabındadır.");
        sheet.Row("son_kayitli_calisma / kullanim_hukmu", "Kullanım kanıtı. Kaydın bulunmaması kullanılmadığını KANITLAMAZ.");
        sheet.Row("paylasilan_alan / baslattigi_is_akisi", "Veri bağları; ayrıntısı veri-analizi.xlsx kitabındadır.");
        sheet.Row("hassas_deger_var", "XAML içinde adres, kullanıcı adı veya parola benzeri değer var. Değerlerin kendisi kısıtlı rapordadır.");
        sheet.Row("Bu çalıştırma",
            string.Create(CultureInfo.InvariantCulture,
                $"{workflows} iş akışı · {live} canlı süreç · {drafts} taslak · {supplied} ürünle gelen · başka bir akışça çağrılan {buildingBlocks}"));
        sheet.Row("Uyarı", "Modeller açıklayıcıdır, çalıştırılabilir değildir. Diyagramlar üretim verisi içerir; kurum dışına çıkarmayın.");
        return sheet;
    }

    public static Sheet Families(int families, int combined, int notCombined, int drafts, int supplied)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "Hangi iş akışlarının aslında aynı işi yaptığına karar vermenize yarar.");
        sheet.Row("Sayfalar", "Aileler (üyeler) · Birleştirme (her ailenin birleşik modeli) · Çiftler (her karşılaştırmanın puanı) · "
            + "Taslaklar ve Ürünle gelenler (gruplamanın dışında tutulanlar)");
        sheet.Row("Nasıl kullanılır",
            "Bir ailenin üyelerini birlesik/ klasöründeki birleşik modeliyle karşılaştırın. Araç yalnızca öneri üretir; "
            + "aynı işi yapıp yapmadıklarına insan karar verir. Kararınızı Aileler sayfasındaki karar sütununa yazın.");
        sheet.Row("baslangica_benzerlik", "Üyenin, ailenin başlangıç noktasına yapısal ve sözcüksel benzerliği (0–1).");
        sheet.Row("zayif_tutarlilik", "evet ise aile gevşek: önce ayırmayı düşünün.");
        sheet.Row("Neden bazıları dışarıda",
            "Taslak bir tanım çalışamaz; ürünle gelen bir akış (yönetilen çözüm) sizin kurmanız gereken bir şey değildir. "
            + "İkisinin de diyagramı yine üretilir, yalnızca gruplamaya girmezler.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"2+ üyeli {families} aile · {combined} birleştirildi · {notCombined} birleştirilmedi · {drafts} taslak · {supplied} ürünle gelen"));
        return sheet;
    }

    public static Sheet Data(int fields, int sharedFields, int cascades, int pairs)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "Hangi iş akışının hangi veriye dokunduğunu ve birbirini nasıl tetiklediğini gösterir.");
        sheet.Row("Sayfalar", "Veri ayak izi (alan alan: yazan, okuyan, tetiklenen) · Tetikleme zincirleri (bir yazmanın başlattığı akışlar)");
        sheet.Row("Neden önemli",
            "Bir alana birden fazla akış yazıyorsa, bunların hangi sırayla çalışacağı CRM'de hiçbir zaman garanti edilmedi. "
            + "Yeni üründe bir sıra kararlaştırılmalı ya da yazan akışlar birleştirilmelidir.");
        sheet.Row("Tetikleme zinciri nedir",
            "İlk akış, ikincinin izlediği bir alana yazdığı ya da bir kayıt oluşturduğu için CRM ikinciyi başlatır. "
            + "Aralarında açık bir çağrı yoktur; bu bağ diyagramlarda görünmez.");
        sheet.Row("kendini_baslatiyor", "evet ise akış kendi yazmasıyla yeniden tetikleniyor: yeni üründe bu döngü elle kırılmalıdır.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"{fields} alan · birden fazla akışın yazdığı {sharedFields} alan · {pairs} çift arasında {cascades} tetikleme zinciri"));
        return sheet;
    }

    public static Sheet External(int activities, int addresses)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "İş akışlarının CRM dışına uzanan çağrılarını gösterir: entegrasyon yükü buradadır.");
        sheet.Row("Sayfalar", "Dış bağımlılıklar (etkinlik başına: kim çağırıyor) · Adresler (iş akışının geçirdiği her adres)");
        sheet.Row("Nasıl çalışır",
            "Bir CRM iş akışı bir servisi kendi başına çağıramaz; tek yol, CRM'e kaydedilmiş özel bir etkinliktir (derlenmiş kod). "
            + "Dışarıya uzanan her çağrı bu yüzden bir satır olarak görünür.");
        sheet.Row("Görülebilen", "Çağrılan etkinliğin adı, hangi iş akışlarının çağırdığı ve iş akışının geçirdiği bağımsız değişkenler — "
            + "adres bunların arasındaysa adres de.");
        sheet.Row("Görülemeyen",
            "Etkinliğin kendi derlemesi içinde ne yaptığı XAML'de yoktur: koda gömülü bir adres buradan görünmez. "
            + "Ayrıca eklentiler (plug-in) iş akışı değildir; bu envantere hiç girmezler. Boş adres sütunu 'hiçbir şey çağırmıyor' demek değildir.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture, $"{activities} özel etkinlik · {addresses} farklı adres"));
        sheet.Row("Uyarı", "Adresler sayfası üretim adresleri taşır; kurum dışına çıkarmayın.");
        return sheet;
    }

    private static Sheet Empty()
    {
        return new Sheet(SheetNames.Guide, "Konu", "Açıklama");
    }
}
