using System.Globalization;

namespace Crm.Cli.Reports;

/// <summary>
/// The "Nasıl okunur" sheet each workbook opens with. What used to be a Markdown file beside the table now travels
/// inside it: a reader who opens one file has the columns, the caveats and the counts in front of them, and there
/// is no second document to keep in step with the first.
/// </summary>
public static class Guides
{
    public static Sheet Plan(int workflows, int live, int excluded, int buildingBlocks)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar",
            "Taşınacak işin listesidir. Her iş akışı için bir satır. Yalnızca sizin kurmanız gereken akışlar buradadır: "
            + "taslaklar, ürünle gelenler ve hiç çalışmamış deneme akışları kapsam-disi.xlsx kitabına ayrıldı, süzmenize gerek yok.");
        sheet.Row("Sayfalar",
            "Taşıma planı (ana liste) · Çağrı ağacı ve Süreç ağaçları (hangi akış hangisini çağırıyor) · "
            + "Okunamayan yapılar (aracın okuyamadığı adımlar) · Sapma (tanım ile çalışan kopya farkı)");
        sheet.Row("Nereden başlanır",
            "Taşıma planı sayfasını baştan okuyun: öncelik 1 canlı süreçlerdir, 2 diyalog, iş kuralı ve süreç akışlarıdır. "
            + "Her satırın bpmn_dosyasi sütunundaki diyagramı açarak akışı görebilirsiniz.");
        sheet.Row("İş yükü nasıl hesaplanır",
            "Bir üst akış ve çağırdığı alt akışlar tek bir taşıma kalemidir. Süreç ağaçları sayfası bu kalemleri gösterir; "
            + "kalem sayısı iş akışı sayısından çok daha azdır.");
        sheet.Row("tetikleyici", "Akışı ne başlatır: kayıt oluşturma, adı verilen alanların güncellenmesi, silme, istek üzerine.");
        sheet.Row("adim / okunamayan_adim", "Akışın büyüklüğü ve aracın okuyamadığı adım sayısı. Okunamayanları elle kontrol edin.");
        sheet.Row("rol", "Giriş noktası bir bütün olarak taşınır; yapı taşı birden çok süreççe paylaşılır, bir kez taşıyın.");
        sheet.Row("aile / aile_rolu / birlesik_dosya", "Birbirine çok benzeyen akışlar ve ailenin birleşik modeli. Ayrıntısı aileler.xlsx kitabındadır.");
        sheet.Row("kullanim_hukmu / son_kayitli_calisma", "Kullanım kanıtı. Kaydın bulunmaması kullanılmadığını KANITLAMAZ.");
        sheet.Row("ozel_etkinlikler", "Yeni üründe karşılığı bulunmayan iş ortağı veya kurum içi kod. Ayrıntısı dis-sistemler.xlsx kitabındadır.");
        sheet.Row("paylasilan_alan / baslattigi_is_akisi", "Veri bağları; ayrıntısı veri-analizi.xlsx kitabındadır.");
        sheet.Row("hassas_deger_var", "XAML içinde adres, kullanıcı adı veya parola benzeri değer var. Değerlerin kendisi kısıtlı rapordadır.");
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
        sheet.Row("Bu kitap ne işe yarar", "Hangi iş akışlarının aslında aynı işi yaptığına karar vermenize yarar.");
        sheet.Row("Sayfalar", "Aileler (üyeler) · Birleştirme (her ailenin birleşik modeli) · Çiftler (her karşılaştırmanın puanı)");
        sheet.Row("Nasıl kullanılır",
            "Bir ailenin üyelerini birlesik/ klasöründeki birleşik modeliyle karşılaştırın. Araç yalnızca öneri üretir; "
            + "aynı işi yapıp yapmadıklarına insan karar verir. Kararınızı Aileler sayfasındaki karar sütununa yazın.");
        sheet.Row("aile", "Ailenin adı, ailenin başlangıç noktası olan iş akışıdır: aileyi incelemeye o akıştan başlayın.");
        sheet.Row("baslangica_benzerlik", "Üyenin, ailenin başlangıç noktasına yapısal ve sözcüksel benzerliği (0–1).");
        sheet.Row("zayif_tutarlilik", "evet ise aile gevşek: önce ayırmayı düşünün.");
        sheet.Row("Neden bazıları yok", "Kapsam dışı akışlar (kapsam-disi.xlsx) gruplamaya hiç girmez.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"2+ üyeli {families} aile · {combined} birleştirildi · {notCombined} birleştirilmedi"));
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

    public static Sheet External(int activities, int addresses, int hosts)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "İş akışlarının CRM dışına uzanan çağrılarını gösterir: entegrasyon yükü buradadır.");
        sheet.Row("Sayfalar", "Dış bağımlılıklar (etkinlik başına: kim çağırıyor) · Adresler (tanımın içinde geçen her adres)");
        sheet.Row("Nasıl çalışır",
            "Bir CRM iş akışı bir servisi kendi başına çağıramaz; tek yol, CRM'e kaydedilmiş özel bir etkinliktir (derlenmiş kod). "
            + "Dışarıya uzanan her çağrı bu yüzden bir satır olarak görünür.");
        sheet.Row("Görülebilen", "Çağrılan etkinliğin adı, hangi iş akışlarının çağırdığı ve iş akışı tanımının herhangi bir yerinde "
            + "yazılı olan adresler — bağımsız değişkende, değişken tanımında ya da bir ifadenin içinde.");
        sheet.Row("Görülemeyen",
            "Etkinliğin kendi derlemesi içinde ne yaptığı XAML'de yoktur: koda ya da konfigürasyona gömülü bir adres buradan "
            + "GÖRÜNMEZ ve çoğu adres oradadır. Bunu yalnızca derlemenin sahibi söyleyebilir; derleme adı yan sütundadır. "
            + "Ayrıca eklentiler (plug-in) iş akışı değildir; bu envantere hiç girmezler.");
        sheet.Row("Boş Adresler sayfası", "\"Hiçbir yere bağlanmıyor\" demek DEĞİLDİR: yalnızca hiçbir adresin tanım metninin "
            + "içine yazılmadığı anlamına gelir.");
        sheet.Row("sunucu", "Adresin işaret ettiği sunucu. Sayfayı bu sütuna göre sıralayın: hangi dış sistemlere dokunulduğu böyle görünür.");
        sheet.Row("adres", "Bulunan adresin kendisi. İçine yazılmış kullanıcı adı ve parola maskelenmiştir (***).");
        sheet.Row("adim_yolu", "Adresin tanım içinde bulunduğu yer. Aynı yol, Okunamayan yapılar sayfasındaki yolla aynı biçimdedir.");
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
