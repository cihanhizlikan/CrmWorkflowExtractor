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
    public static Sheet Plan(int workflows, int live, int excluded, int buildingBlocks, int stages, int stageMaps, int firedByStage)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar",
            "Taşınacak işin listesidir: önce taleplerin geçtiği aşamalar, sonra her iş akışı için bir satır. Yalnızca sizin "
            + "kurmanız gerekenler buradadır, süzmenize gerek yok.");
        sheet.Row("İki tür süreç",
            "TALEP AŞAMASI: bir talebin (müşteri isteğinin) geçtiği adım. Her aşama olumlu, olumsuz ya da iptal sonucuyla "
            + "kapanır ve sonuca göre bir sonraki aşamaya geçer. İŞ AKIŞI: CRM'in kendiliğinden yaptığı iş; bir aşamanın "
            + "sonucunda, bir kayıt olayında ya da başka bir iş akışının çağrısıyla çalışır. Aşamalar süreci, iş akışları o "
            + "sürecin adımlarını anlatır: önce aşamaları okuyun.");
        sheet.Row("Sayfa sırası",
            "Talep konuları (hangi süreçler var, hangi haritayı açmalı) → Talep aşamaları (her aşamanın sonuçları ve kuralları) → "
            + "İş akışları (her iş akışı için bir satır) → İş akışı ağaçları (birbirini çağıran iş akışlarını bir taşıma "
            + "kalemi olarak gösterir). Kılavuz da bu sırayı izler.");
        sheet.Row("Diyagramlar",
            "Her talep konusunun aşama haritası bpmn/talep-asamalari/ altındadır ve o konunun iş akışlarını iç içe taşır. "
            + "Her iş akışının kendi diyagramı bpmn/is-akislari/<kategori>/<kayıt türü>/ altındadır. Bu kitaptaki her yol "
            + "paketin kökünden yazılmıştır; dosyayı doğrudan açabilirsiniz.");
        sheet.Row("Elinizdekinden diyagrama",
            "Aşama adı → Talep aşamaları'nda bulun, haritadaki_ad metnini asama_haritasi dosyasında aratın. İş akışı adı → "
            + "İş akışları'nda bulun; calistiran_asamalar doluysa önce o haritayı açın. Alan ya da kayıt türü → "
            + "veri-analizi.xlsx. Adres → dis-sistemler.xlsx Adresler sayfası, sonra bpmn_dosyasi. Kimlik (asama_id, "
            + "is_akisi_id) → .bpmn dosyalarının içinde metin olarak aranabilir.");

        sheet.Row("— Talep konuları sayfası —", "Her aşama haritası için bir satır, en çok aşaması olandan başlayarak.");
        sheet.Row("talep_konusu · baslangic_asamasi", "Talebin konusu ve talebin başladığı aşama. Harita bu aşamadan çizilir.");
        sheet.Row("asama_sayisi · is_akisi_sayisi · toplam_adim",
            "Sürecin büyüklüğü: haritadaki aşama sayısı, yolda çalışan farklı iş akışı sayısı ve bu iş akışlarının toplam adım "
            + "sayısı. Hangi süreçten başlayacağınızı ve ne kadar süreceğini buradan tahmin edin. Birden fazla konunun "
            + "çalıştırdığı bir iş akışı her konuda sayılır.");
        sheet.Row("is_akislari · asama_haritasi", "Haritada çalışan iş akışlarının adları ve açılacak dosya.");

        sheet.Row("— Talep aşamaları sayfası —",
            string.Create(CultureInfo.InvariantCulture, $"Bu çalıştırmada {stages} etkin aşama, {stageMaps} aşama haritası. ")
            + "Satırlar talebin aşamalarla karşılaştığı sıradadır.");
        sheet.Row("talep_konusu · asama · haritadaki_ad",
            "Aşamanın ait olduğu konu ve adı. haritadaki_ad, aşamanın kutusunda yazan metindir: haritada bu metni aratın. "
            + "Aynı adı taşıyan iki ayrı aşama haritada adım kodu ya da sıra numarasıyla ayrılır.");
        sheet.Row("baslangic_asamasi · otomatik_ilerler",
            "baslangic_asamasi evet ise o konudaki talepler bu aşamada başlar ve aşama haritası buradan çizilir. "
            + "otomatik_ilerler evet ise aşama bir kişiyi beklemeden ilerler; yeni üründe bir otomasyon olarak kurulmalıdır.");
        sheet.Row("olumlu_* · olumsuz_* · iptal_*", "Her sonuç için: talebin geçtiği sonraki aşama, çalıştırılan iş akışı ve müşteriye "
            + "gönderilen SMS. \"(pasif)\" yazan sonraki aşama kapatılmış, \"(aşama kaydı yok)\" yazan silinmiştir: ikisi de bugün çıkmaz "
            + "bir yoldur, yeni üründe aynen kurmadan önce sorun.");
        sheet.Row("kullaniciya_atanir · kuyruk · takim", "Aşamanın işi kime düşürdüğü. Belirli bir kişiye atanıyorsa kişinin adı bu "
            + "pakette yoktur; yalnızca öyle olduğu yazılır.");
        sheet.Row("sla_* · calisilan_gun · asama_sla", "Aşamanın süre kuralı ve süre aşıldığında ne olduğu: kapatılıp kapatılmadığı, "
            + "hangi statüye geçtiği, yöneticiye bildirilip bildirilmediği. Yeni üründe her biri bir kuraldır.");
        sheet.Row("gecis_dokuman_tipi · dokuman_*", "Aşamaya geçmek için istenen doküman ve doküman geldiğinde aşamanın kendiliğinden "
            + "kapanıp kapanmadığı.");
        sheet.Row("diger_kurallar", "Aşamanın taşıdığı diğer kurallar (otomatik çözüm, geri dönüş, kapanış, atlanabilirlik, limitler, "
            + "kampanya, adım kodu, mobil şube metinleri), CRM'deki etiketleriyle ve yalnızca doluysa. Boş hücre o kuralların bu "
            + "aşamada kullanılmadığı demektir.");
        sheet.Row("asama_haritasi · asama_id",
            "Aşamanın göründüğü aşama haritaları ve CRM'de aramak için kimliği. Haritada olumlu sonuç sağa, olumsuz aşağıdan, "
            + "iptal yukarıdan çıkar; aynı yere giden sonuçlar tek oktur (\"olumsuz / iptal\"). Geri dönüşler \"… aşamasına dön\" "
            + "bağlantı olayında biter ve aynı adlı \"… aşamasına dönüş\" olayından devam eder. İş akışı kutularının içine "
            + "girilerek adımları görülür; kalın kenarlı kutu, aynı iş akışının haritada ilk geçtiği kutuyu gösterir.");

        sheet.Row("— İş akışları sayfası —", "Her iş akışı için bir satır: önce canlı süreçler, sonra diyalog ve iş kuralları.");
        sheet.Row("is_akisi · kategori · kayit_turu", "Ne olduğu ve hangi kayıt türü (poliçe, müşteri, talep…) üzerinde çalıştığı. "
            + "İşi kayıt türüne göre bölerken bu sütunu kullanın.");
        sheet.Row("tetikleyici", "Hangi kayıt olayı başlatır. \"kayıt olayı yok\" ise iş akışını bir aşama, başka bir iş akışı ya da "
            + "bir kullanıcı başlatır. İlk tasarım kararı budur: yeni üründe aynı olayın karşılığı var mı?");
        sheet.Row("calistiran_asamalar", string.Create(CultureInfo.InvariantCulture,
            $"Bu iş akışını çalıştıran talep aşamaları ve sonuçları ({firedByStage} iş akışında dolu). ")
            + "Doluysa iş akışı bir aşamanın sonucunda çalışır: önce o aşamanın haritasını açın ve onu o haritanın bir adımı olarak tasarlayın.");
        sheet.Row("adim_sayisi", "İş akışının büyüklüğü. Tahmin için; sayı büyüdükçe kalem büyür.");
        sheet.Row("bekleme_var", "evet ise iş akışı bir zamanlayıcı ya da bir koşul bekliyor: süreç saatlerce, günlerce açık kalır. "
            + "Zamana yayılan bir süreç, baştan sona koşan bir süreçle aynı şey değildir; tasarımı buna göre kurun.");
        sheet.Row("cagri_rolu", "\"aşama adımı\" bir talep aşamasının sonucunda çalışır; aşama haritasıyla birlikte tasarlanır. "
            + "\"yapı taşı\" başka iş akışlarınca çağrılır: tek başına taşımayın, bir kez taşıyın. \"giriş noktası\" tek "
            + "başına bir süreçtir.");
        sheet.Row("cagiran_is_akislari · cagirdigi_is_akislari", "Bu iş akışını çağıranlar ve onun çağırdığı alt iş akışları. "
            + "Birlikte tek bir taşıma kalemidir; İş akışı ağaçları sayfası kalemin tamamını gösterir.");
        sheet.Row("kullanim · son_kayitli_calisma",
            "Canlı mı: \"çalışıyor\", \"kayıtlı çalışma yok\" ya da \"bilinemez\"; son kayıtlı çalışmanın tarihi yanındaki sütundadır. "
            + "Hücrede \"<tarih> sonrası çalışma yok\" yazıyorsa kanıt yalnızca o tarihten bugüne tarandı; o iş akışı daha önce "
            + "çalışmış olabilir. DİKKAT: kaydın bulunmaması kullanılmadığını KANITLAMAZ — CRM sistem işlerini düzenli olarak "
            + "siler, iş kuralları hiç iz bırakmaz, gerçek zamanlı akışlar yalnızca hatayı kaydeder. \"kayıtlı çalışma yok\" bir "
            + "silme gerekçesi değildir.");
        sheet.Row("okunamayan_adim_sayisi", "Sıfırsa çizim tamdır. Sıfırdan büyükse diyagramda o adımlar OKUNAMADI olarak işaretlidir; "
            + "o iş akışını bitirmeden CRM'de karşılıklarına bakın.");
        sheet.Row("calisan_kopya_farkli", "evet ise CRM'de çalışan kopya bu tanımdan farklıdır: diyagram üretimdeki davranışı "
            + "göstermeyebilir, CRM'deki çalışan kopyayı esas alın. Boşsa bu çalıştırma karşılaştırma yapamadı.");
        sheet.Row("ozel_etkinlikler", "Dışarıya uzanan çağrılar. Doluysa ayrı bir entegrasyon kalemi açın — ayrıntısı dis-sistemler.xlsx.");
        sheet.Row("tetikledigi_is_akisi_sayisi · paylasilan_alan",
            "Yazmalarıyla kaç iş akışını kendiliğinden başlattığı ve kaç alanı başkalarıyla paylaştığı. İkisi de sıfırdan büyükse "
            + "bu iş akışı tek başına tasarlanamaz — veri-analizi.xlsx.");
        sheet.Row("yazdigi_varliklar", "Neye dokunduğu. Yeni tasarımın dış dünyaya verdiği sözdür; alan alan dökümü veri-analizi.xlsx'tedir.");
        sheet.Row("calisma_modu", "\"Gerçek zamanlı\" iş akışı kullanıcıyı bekletir, \"arka plan\" bekletmez. Aynı işi arka plana almak "
            + "davranışı değiştirir.");
        sheet.Row("elle_baslatilabilir",
            "evet ise kullanıcı bu iş akışını arayüzden başlatabilir; yeni üründe bunun bir karşılığı (bir düğme, bir eylem) "
            + "kurulmalıdır. \"hayır\" ise yalnızca CRM tetikler; yeni tasarımda aranacak şey tetikleyicidir.");
        sheet.Row("calisma_kimligi",
            "Adımların KİMİN yetkileriyle çalıştığı. \"Sahip\" ise iş akışı her zaman sahibinin yetkileriyle çalışır; "
            + "\"Çağıran Kullanıcı\" ise başlatanın yetkileriyle çalışır ve yetkisi yetmeyen bir kullanıcıda ADIM BAŞARISIZ OLUR. "
            + "İkisi farklı tasarımlardır.");
        sheet.Row("hassas_deger_var", "XAML içinde adres, kullanıcı adı veya parola benzeri değer var. Değerlerin kendisi bu pakette "
            + "yoktur; kurum içinde bilgi güvenliği ekibindedir. Tasarım için değeri bilmeniz gerekmez, bir sır taşındığını bilmeniz yeter.");
        sheet.Row("bpmn_dosyasi · is_akisi_id", "İş akışının diyagramı ve CRM'de aramak için kimliği.");

        sheet.Row("— İş akışı ağaçları sayfası —",
            "Bir üst iş akışı ve çağırdığı alt iş akışları TEK bir taşıma kalemidir. Bu sayfa kalemleri gösterir: kok_is_akisi "
            + "kalemin adı, derinlik çağrı zincirindeki yeri. Kalem sayısı iş akışı sayısından çok daha azdır. Sayfa boşsa "
            + "hiçbir iş akışı başkasını çağırmıyor demektir.");
        sheet.Row("Bu çalıştırma",
            string.Create(CultureInfo.InvariantCulture,
                $"{workflows} taşınacak iş akışı · {live} canlı süreç · {firedByStage} tanesini bir talep aşaması çalıştırıyor · başka bir iş akışınca çağrılan {buildingBlocks} · plan dışı {excluded}"));
        sheet.Row("Uyarı", "Modeller açıklayıcıdır, çalıştırılabilir değildir. Diyagramlar üretim verisi içerir; kurum dışına çıkarmayın.");
        return sheet;
    }

    public static Sheet Excluded(int excluded, int drafts, int supplied, int all)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar",
            "Taşıma planından (tasima-plani.xlsx) ÇIKARILAN iş akışlarıdır. Buradaki hiçbir satır için iş planlamayın; kitap yalnızca "
            + "bir çıkarma kararını sorgulamak istediğinizde açılır.");
        sheet.Row("neden", "Çıkarma gerekçesi. Yalnızca üç kesin gerekçe kullanılır; şüphe varsa akış planda bırakılmıştır.");
        sheet.Row("ürünle gelmiş", "CRM bu akışı yönetilen bir çözümün parçası olarak bildiriyor: ürünle birlikte gelmiş, kurum yazmamış.");
        sheet.Row("taslak", "Tanım taslak durumda; CRM taslak bir tanımla yeni çalıştırma başlatmaz.");
        sheet.Row("adı deneme gibi", "Adı DRAFT/TEST/kopya gibi okunuyor VE kanıt dosyasında hiç kayıtlı çalışması yok. "
            + "Yalnızca ad yeterli değildir: adı deneme gibi görünen ve üretimde çalışan akışlar planda kaldı.");
        sheet.Row("Diyagramı yine de var mı", "Evet. Her iş akışının BPMN dosyası kapsam-disi-bpmn/ altında üretildi; bpmn_dosyasi sütunundadır.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"{all} iş akışının {excluded} tanesi kapsam dışı · {drafts} taslak · {supplied} ürünle gelen"));
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

    public static Sheet External(int activities, int addresses, int hosts, int assemblyAddresses, int pluginSteps)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "İş akışlarının CRM dışına uzanan çağrılarını gösterir: entegrasyon yükü buradadır.");
        sheet.Row("Sayfa sırası", "Dış bağımlılıklar (etkinlik başına: kim çağırıyor, nereye gidiyor) → Adresler (tanımın "
            + "içinde geçen her adres) → Eklentiler (iş akışı olmayan, mesaj üzerinde çalışan kod)");
        sheet.Row("Nasıl çalışır",
            "Bir CRM iş akışı bir servisi kendi başına çağıramaz; tek yol, CRM'e kaydedilmiş özel bir etkinliktir (derlenmiş kod). "
            + "Dışarıya uzanan her çağrı bu yüzden bir satır olarak görünür.");
        sheet.Row("etkinlik · derleme", "Çağrılan kodun adı ve içinde bulunduğu derleme. Yeni üründe her birinin karşılığını kurmanız gerekir; "
            + "ne yaptığını yalnızca derlemenin sahibi söyleyebilir.");
        sheet.Row("cagiran_is_akisi_sayisi · cagiran_is_akislari", "Kaç akışı etkiliyor. Üstteki satırlar en çok akışı etkileyenlerdir: oradan başlayın.");
        sheet.Row("derlemedeki_adresler",
            "Etkinliğin geldiği DERLEMENİN İÇİNDE yazılı adresler. Aradığınız servis adresi neredeyse her zaman buradadır, "
            + "çünkü iş akışı onu geçirmez, kod kendi içinde tutar. Okuma şekli: \"bu adımın çalıştırdığı kodun içinde şu "
            + "adresler var\" — \"bu adım şu adrese gidiyor\" DEĞİL. Kod çalışırken adresi parçalardan birleştiriyorsa ya da "
            + "bir ayar kaydından okuyorsa burada görünmez.");
        sheet.Row("kayitli",
            "Bu etkinlik CRM'de hâlâ kayıtlı mı. hayır ise iş akışı artık var olmayan bir koda gidiyor demektir: "
            + "üretimde kırık bir çağrıdır, önce onu sorun.");
        sheet.Row("parametreler",
            "Etkinliğin iş akışından aldığı ve ona geri verdiği adların tamamı — CRM'in tuttuğu en yakın şey bir imzadır. "
            + "Adresi söylemez ama ARKADAKİ OPERASYONU söyler: \"GetPersonEntityInformationRq\" ya da \"ApproveClaimFundSellResult\" "
            + "gibi bir ad, yeni üründe hangi servis çağrısının kurulacağını tarif eder. Yeni tasarımın entegrasyon "
            + "maddelerini bu sütundan çıkarın.");
        sheet.Row("parametrelerde kimlik bilgisi",
            "Bu sütunda UserName, PassWord, FtpUserID gibi bir ad görürseniz, kimlik bilgisi iş akışı tanımının içine "
            + "yazılmış demektir. Değerlerin kendisi bu pakette yoktur; bulguyu Kurumsal Mimari'ye iletin, oradan bilgi "
            + "güvenliği ekibine gider.");
        sheet.Row("Görülemeyen",
            "Etkinliğin kendi derlemesi içinde ne yaptığı XAML'de yoktur: koda ya da konfigürasyona gömülü bir adres buradan "
            + "GÖRÜNMEZ ve çoğu adres oradadır. Ayrıca eklentiler (plug-in) iş akışı değildir; bu envantere hiç girmezler.");
        sheet.Row("Boş Adresler sayfası", "\"Hiçbir yere bağlanmıyor\" demek DEĞİLDİR: yalnızca hiçbir adresin tanım metninin "
            + "içine yazılmadığı anlamına gelir.");
        sheet.Row("sunucu", "Adresin işaret ettiği sunucu. Sayfayı bu sütuna göre sıralayın: hangi dış sistemlere dokunulduğu böyle görünür.");
        sheet.Row("adres", "Bulunan adresin kendisi. İçine yazılmış kullanıcı adı ve parola maskelenmiştir (***).");
        sheet.Row("Eklentiler sayfası",
            "Eklentiler iş akışı değildir: CRM'de bir mesaj üzerinde çalışan koddur ve taşıma planında hiç görünmezler. "
            + "Yeni üründe karşılıkları ayrıca kurulmalıdır. konfigurasyondaki_adresler, kayıt sırasında verilmiş "
            + "\"unsecure configuration\" metninde geçen adreslerdir; gizli konfigürasyon okunmaz, orada kimlik bilgisi tutulur.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"{activities} özel etkinlik · {hosts} farklı sunucu · {addresses} farklı adres · "
            + $"derlemelerin içinde {assemblyAddresses} adres · {pluginSteps} eklenti adımı"));
        sheet.Row("Uyarı", "Adresler sayfası üretim adresleri taşır; kurum dışına çıkarmayın.");
        return sheet;
    }

    public static Sheet RunAuthority(int roles, int users, int teams, int onDemand, int inPlan, string? note)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar", "Bir süreci kimin elle başlatabildiğini ve başladığında KİMİN yetkileriyle "
            + "çalıştığını gösterir. İkincisi yeni tasarımda bir karardır: adımların hangi hesabın gözüyle okuyup yazacağı.");
        sheet.Row("Önce şunu bilin",
            "CRM'de \"şu rol şu iş akışını çalıştırabilir\" diye bir kayıt YOKTUR. Elle başlatma tek bir yetkiye bağlıdır "
            + "(prvExecuteWorkflowJob) ve bu yetki bütün süreçler için aynı anda verilir. Bu yüzden cevap iki sayfaya "
            + "bölünmüştür: yetkiyi taşıyan roller, ve iş akışı başına gerçekten değişen iki bilgi.");
        sheet.Row("Sayfa sırası", "Çalıştırma yetkisi (yetkiyi kim taşıyor) → Kim çalıştırabilir (iş akışı başına: "
            + "elle başlatılabilir mi, kimin kimliğiyle çalışır)");
        sheet.Row("rol · kullanici_sayisi · ekipler",
            "Elle çalıştırma yetkisini taşıyan güvenlik rolü ve onu taşıyan kişi sayısı ile ekipler. Sorduğunuz "
            + "\"hangi kullanıcı grubu\" budur: ekip adları kullanıcı adlarından daha kullanışlıdır ve kalıcıdır. "
            + "Kişi adları bu pakete hiç çıkarılmaz; yalnızca sayılır.");
        sheet.Row("calistirma_derinligi",
            "Yetkinin nereye kadar ulaştığı: Kullanıcı (yalnızca kendi kayıtları) · İş birimi · İş birimi ve altı · Kurum. "
            + "Kurum dışındaki bir derinlik, rolün her süreci başlatamayacağı anlamına gelir.");
        sheet.Row("surec_gorme_derinligi",
            "Rolün kaç süreç kaydını GÖREBİLDİĞİ. Görülmeyen bir süreç elle başlatılamaz, yetki dursa bile. Bunu "
            + "diğer sayfadaki kaydin_is_birimi sütunu ile birlikte okuyun.");
        sheet.Row("elle_baslatilabilir",
            "evet ise kullanıcı bu süreci arayüzden başlatabilir; yeni üründe bunun bir karşılığı (bir düğme, bir "
            + "eylem) kurulmalıdır. \"hayır\" ise süreci kimse elle başlatmaz: yalnızca CRM tetikler, ve yeni tasarımda "
            + "aranacak şey tetikleyicidir, yetki değil.");
        sheet.Row("calisma_kimligi",
            "Adımların KİMİN yetkileriyle çalıştığı. \"Sahip\" ise süreç her zaman sahibinin gözüyle çalışır — kullanıcı "
            + "kim olursa olsun. \"Çağıran Kullanıcı\" ise süreci başlatanın yetkileriyle çalışır, ve yetkisi yetmeyen "
            + "bir kullanıcıda ADIM BAŞARISIZ OLUR. İkisi farklı tasarımlardır; satırı yazmadan geçmeyin.");
        sheet.Row("sahip · sahip_turu",
            "calisma_kimligi \"Sahip\" olduğunda adımların kullandığı hesap. Yeni üründe bu hesabın karşılığı "
            + "kurulmalıdır. sahip_turu ekip ise yetki bir kişiye değil bir ekibe bağlıdır; bu daha sağlamdır ve "
            + "yeni tasarımda da tercih edilmelidir.");
        sheet.Row("kaydin_is_birimi",
            "Sürecin KAYDININ bağlı olduğu iş birimi. Rolün süreç görme derinliği buna göre ölçülür: \"İş birimi\" "
            + "derinliğindeki bir rol yalnızca kendi birimindeki süreçleri görür.");
        sheet.Row("Görülemeyen",
            "Bir sürecin tek bir kullanıcıyla ya da ekiple PAYLAŞILMIŞ olması. Paylaşım kayıtları Web API üzerinden "
            + "okunamaz; paylaşımla verilmiş bir erişim bu listede hiç görünmez. Bu listeyi \"en az bunlar\" diye okuyun.");
        sheet.Row("Bu çalıştırma", string.Create(CultureInfo.InvariantCulture,
            $"{roles} rol elle çalıştırma yetkisi taşıyor · bu rollerde {users} kullanıcı ve {teams} ekip üyeliği · "
            + $"plandaki {inPlan} süreçten {onDemand} tanesi elle başlatılabiliyor"));
        if (!string.IsNullOrEmpty(note))
        {
            sheet.Row("Eksik", note);
        }
        return sheet;
    }

    private static Sheet Empty()
    {
        return new Sheet(SheetNames.Guide, "Konu", "Açıklama");
    }
}
