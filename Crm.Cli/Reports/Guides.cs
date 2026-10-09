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
    public static Sheet Plan(int workflows, int live, int excluded, int buildingBlocks, int stages, int stageMaps)
    {
        Sheet sheet = Empty();
        sheet.Row("Bu kitap ne işe yarar",
            "Taşınacak işin listesidir: önce talebin geçtiği aşamalar, sonra her iş akışı için bir satır. Yalnızca sizin "
            + "kurmanız gerekenler buradadır, süzmenize gerek yok. Plandan çıkarılanlar ve gerekçeleri Kurumsal Mimari'de ayrı bir dosyadadır.");
        sheet.Row("Sayfa sırası",
            "Aşamalar (talebin geçtiği süreç) → Taşıma planı (iş akışları) → Çağrı ağacı (bir akış tek başına mı) → "
            + "Süreç ağaçları (işi kalemlere böl) → Sapma (çalışan kopya çizimden farklı) → Okunamayan yapılar (çizimin eksik yeri). "
            + "Kılavuz da bu sırayı izler.");
        sheet.Row("Aşamalar sayfası",
            $"Bu çalıştırmada {stages} etkin aşama, {stageMaps} aşama akışı. Bir talep, konusunun (alt kategorisinin) BİRİNCİL "
            + "aşamasında başlar; her aşama olumlu, olumsuz ya da iptal sonucuyla kapanır ve sonuca göre bir sonraki aşamaya geçer, "
            + "yolda bir iş akışı çalıştırabilir. İş akışları bu makinenin adımlarıdır: önce aşamaları anlayın, sonra iş akışlarını. "
            + "Satırlar talebin aşamalarla karşılaştığı sıradadır.");
        sheet.Row("surec · asama · kisa_ad", "Aşamanın ait olduğu konu ve adı. Diyagramda aşama kısa adıyla görünür.");
        sheet.Row("birincil · otomatik", "birincil evet ise o konudaki talepler bu aşamada başlar ve aşama akışının diyagramı buradan "
            + "çizilir. otomatik evet ise aşama bir kişiyi beklemeden ilerler; yeni üründe bir otomasyon olarak kurulmalıdır.");
        sheet.Row("olumlu_* · olumsuz_* · iptal_*", "Her sonuç için: talebin geçtiği sonraki aşama, çalıştırılan iş akışı ve müşteriye "
            + "gönderilen SMS. \"(pasif)\" yazan sonraki aşama kapatılmış, \"(aşama kaydı yok)\" yazan silinmiştir: ikisi de bugün çıkmaz "
            + "bir yoldur, yeni üründe aynen kurmadan önce sorun.");
        sheet.Row("kullaniciya_atanir · kuyruk · takim", "Aşamanın işi kime düşürdüğü. Belirli bir kişiye atanıyorsa kişinin adı bu "
            + "pakette yoktur; yalnızca öyle olduğu yazılır.");
        sheet.Row("sla_* · calisilan_gun · asama_sla", "Aşamanın süre kuralı ve süre aşıldığında ne olduğu: kapatılıp kapatılmadığı, "
            + "hangi statüye geçtiği, yöneticiye bildirilip bildirilmediği. Yeni üründe her biri bir kuraldır.");
        sheet.Row("gecis_dokuman_tipi · dokuman_*", "Aşamaya geçmek için istenen doküman ve doküman geldiğinde aşamanın kendiliğinden "
            + "kapanıp kapanmadığı.");
        sheet.Row("Diğer aşama sütunları", "otomatik_cozum_*, geri_donus, ana_talep_yazilabilir, bu_asama_kapanis, atlanarak_gecilebilir, "
            + "servis_talebi_limiti, kampanya_grubu, step_code, tanim, mobil_sube_*: aşamanın taşıdığı diğer kurallar, CRM'deki "
            + "etiketleriyle. Boş hücre o kuralın bu aşamada kullanılmadığı demektir.");
        sheet.Row("asama_akisi_bpmn · asama_id", "Aşamanın göründüğü aşama akışı diyagramları ve CRM'de aramak için kimliği. Diyagramda "
            + "olumlu sonuç sağa, olumsuz aşağıdan, iptal yukarıdan çıkar; iş akışı kutularının içine girilerek adımları görülebilir.");
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
        sheet.Row("kullanim · son_kayitli_calisma",
            "Canlı mı: \"çalışıyor · tarih\", \"kayıtlı çalışma yok\" ya da \"bilinemez\". Hücrede \"<tarih> sonrası çalışma yok\" "
            + "yazıyorsa kanıt yalnızca o tarihten bugüne tarandı; daha eskisine bakılmadı, o akış daha önce çalışmış olabilir. "
            + "DİKKAT: kaydın bulunmaması kullanılmadığını KANITLAMAZ — CRM sistem işlerini düzenli olarak siler, iş kuralları "
            + "hiç iz bırakmaz, gerçek zamanlı akışlar yalnızca hatayı kaydeder. \"kayıtlı çalışma yok\" bir silme gerekçesi değildir.");
        sheet.Row("okunamayan_adim", "Sıfırsa çizim tamdır. Sıfırdan büyükse çizim eksiktir: diyagramda o adımlar OKUNAMADI olarak "
            + "işaretlidir; o akışı bitirmeden CRM'de karşılıklarına bakın. Hangi yapının okunamadığı Okunamayan yapılar sayfasındadır.");
        sheet.Row("ozel_etkinlikler", "Dışarıya uzanan çağrılar. Doluysa ayrı bir entegrasyon kalemi açın — ayrıntısı dis-sistemler.xlsx.");
        sheet.Row("baslattigi_is_akisi · paylasilan_alan",
            "Kaç akışı tetikliyor ve kaç alanı başkalarıyla paylaşıyor. İkisi de sıfırdan büyükse bu akış tek başına tasarlanamaz — veri-analizi.xlsx.");
        sheet.Row("yazdigi_varliklar", "Neye dokunduğu. Yeni tasarımın dış dünyaya verdiği sözdür; alan alan dökümü veri-analizi.xlsx'tedir.");
        sheet.Row("mod", "\"Gerçek zamanlı\" akış kullanıcıyı bekletir, \"arka plan\" bekletmez. Aynı işi arka plana almak davranışı değiştirir.");
        sheet.Row("hassas_deger_var", "XAML içinde adres, kullanıcı adı veya parola benzeri değer var. Değerlerin kendisi bu pakette "
            + "yoktur; kurum içinde bilgi güvenliği ekibindedir. Tasarım için değeri bilmeniz gerekmez, bir sır taşındığını bilmeniz yeter.");
        sheet.Row("bpmn_dosyasi · is_akisi_id", "Nereye gideceğiniz: akışın diyagramı ve CRM'de aramak için kimliği.");
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
