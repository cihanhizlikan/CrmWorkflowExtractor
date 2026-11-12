using System.Globalization;
using Crm.Cli.Stages;
using Crm.Ir.Model;

namespace Crm.Cli.Reports;

/// <summary>
/// <c>raporlar/nasil-kullanilir.docx</c>: the one thing an analyst who has never seen this CRM reads first. It
/// says what the package is, which file to open in which order, and how to work through a single workflow from
/// the plan row to a drawn process — demonstrated on a real workflow from THIS run, not on an invented one.
///
/// <para>
/// It is a Word document in the department's own standard layout (AHE-BT-EY-STD), so it files beside the
/// organisation's other process documents and the reader can annotate it. Everything in it that could go stale —
/// the counts, the example, the file names — is taken from the run that writes it.
/// </para>
///
/// <para>
/// It names only what the reader is given, because a guide that named anything else would send them looking for
/// it — the delivery list is fixed by <c>DeliveryTests</c> and this guide's own tests.
/// </para>
/// </summary>
public static class AnalystGuide
{
    private const string Title = "CRM Talep Aşamaları ve İş Akışları — Taşıma Kılavuzu";

    /// <summary>
    /// What the guide says of itself before anything else: the reader is an outsource team acting on a document
    /// a tool wrote from production data, and nothing in it has been checked by hand row by row.
    /// </summary>
    public const string Disclaimer = "Bu çalışma hata içerebilir ve yalnızca referans amaçlıdır.";

    /// <summary>
    /// The guide's own revisions, newest last. The partner refers to the document by its number ("v1.1"), so a
    /// change to what the guide SAYS adds a row here; a re-run over new data does not, because the run and its
    /// date are on the cover already. The build stamp stays in the internal report, where it is read.
    /// </summary>
    private static readonly Revision[] Revisions =
    [
        new("v1.0", "2 Ekim 2026", "İlk yayın, kurumun standart Word düzeninde."),
        new("v1.1", "12 Kasım 2026", "Sürüm numarası ve sorumluluk reddi eklendi."),
    ];

    /// <summary>The guide's current version, as the cover and the revision table show it.</summary>
    public static string Edition
    {
        get { return Revisions[^1].Number; }
    }

    public static byte[] Build(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage, string logoFile)
    {
        List<DocumentPart> parts = [];
        PngImage? logo = Logo(state, logoFile);
        byte[]? image = LogoBytes(logoFile);
        int inScope = MigrationPlan.InScope(state, documents, usage).Count();

        Cover(parts, state, documents, inScope, logo);
        Contents(parts);
        Introduction(parts, documents.Count, inScope);
        Files(parts);
        StageWalkthrough(parts, state);
        Walkthrough(parts, state, usage, Example(state, documents, usage));
        Traps(parts);
        Checklist(parts);
        Glossary(parts);
        return WordDocument.Build(parts, image, Disclaimer + " · " + Title + " · " + Edition);
    }

    private static void Cover(List<DocumentPart> parts, RunState state, IReadOnlyList<WorkflowIr> documents, int inScope, PngImage? logo)
    {
        if (logo is PngImage picture)
        {
            parts.Add(new Logo(picture.Width, picture.Height, 160));
        }
        parts.Add(new Paragraph("CoverMainTitle", Title));
        parts.Add(new Paragraph("Normal", ""));
        parts.Add(new Paragraph("CoverDocInfo", "Kurumsal Mimari", "Hazırlayan: "));
        parts.Add(new Paragraph("CoverDocInfo", Title, "Doküman Adı: "));
        parts.Add(new Paragraph("CoverDocInfo", Stamp(state.RunId), "Yayın Tarihi: "));
        parts.Add(new Paragraph("CoverDocInfo", Edition, "Sürüm: "));
        parts.Add(new Paragraph("CoverDocInfo", state.RunId, "Çalıştırma: "));
        parts.Add(new Paragraph("Normal", ""));
        parts.Add(new Paragraph("Disclaimer", "Uyarı: " + Disclaimer));
        parts.Add(new Paragraph("Normal", "Bu belge, kurumun CRM sisteminde çalışan " + Number(documents.Count)
            + " süreç tanımının okunmuş ve çizilmiş halini anlatır. Bunların " + Number(inScope)
            + " tanesi yeni üründe yeniden kurulacak iştir; taleplerin geçtiği aşamalar " + Number(state.StageMapPaths.Count)
            + " aşama haritasında çizilmiştir. CRM'i daha önce görmemiş, ancak BPMN okuyabilen bir sistem analisti bu belgeyi "
            + "baştan sona okuduktan sonra ilk süreci aynı gün çözümleyebilir."));
        parts.Add(new Table(
        [
            new DocumentRow(["Revizyon Tarihi", "Yazan", "Revizyon No", "Revizyon Açıklaması"], Header: true),
            .. Revisions.Select(revision => new DocumentRow([revision.Date, "Kurumsal Mimari", revision.Number, revision.Change]))
        ], [2200, 2200, 1800, 3400]));
        parts.Add(new Paragraph("Normal", "Paketteki tüm içerik üretim verisinden hazırlanmıştır; bu nedenle kurum dışına "
            + "çıkarılmamasını rica ederiz. Diyagramlar süreçleri anlamak ve yeniden tasarlamak için hazırlanmıştır; "
            + "çalıştırılamaz ve CRM'e geri yüklenemezler."));
        parts.Add(new PageBreak());
    }

    /// <summary>
    /// A contents list written out rather than a Word field: a field arrives empty and asks the reader to update
    /// it, and a reader who has never opened the package should not have to repair it before reading it.
    /// </summary>
    private static void Contents(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "İçindekiler"));
        foreach (string line in new[]
        {
            "1  Giriş", "    1.1  Amaç ve kapsam", "    1.2  Kısa arka plan",
            "2  Dosyalar ve açılış sırası", "    2.1  tasima-plani.xlsx", "    2.2  bpmn/ — diyagramlar ve gösterim",
            "    2.3  veri-analizi.xlsx", "    2.4  dis-sistemler.xlsx", "    2.5  Elinizdekinden diyagrama",
            "3  Bir talep konusunu adım adım çözümleme", "4  Bir iş akışını adım adım çözümleme",
            "5  Dikkat edilmesi gereken noktalar", "6  Tamamlamadan önce kontrol listesi", "7  Sözlük"
        })
        {
            parts.Add(new Paragraph("Normal", line));
        }
        parts.Add(new PageBreak());
    }

    private static void Introduction(List<DocumentPart> parts, int total, int inScope)
    {
        parts.Add(new Paragraph("Heading1", "1  Giriş"));
        parts.Add(new Paragraph("Heading2", "1.1  Amaç ve kapsam"));
        parts.Add(new Paragraph("Normal", "Bu paket, CRM'deki süreçlerin yeni bir ürüne taşınması için hazırlanmıştır. "
            + "İçinde taşınacak işin listesini, her sürecin çizilmiş halini ve süreçlerin veriye ve dış sistemlere nasıl "
            + "dokunduğunu bulacaksınız. Amacımız, CRM ekranlarını açmanıza gerek kalmadan bir sürecin ne yaptığını "
            + "anlayabilmeniz ve yeni tasarımı bu belgelerden çıkarabilmenizdir."));
        parts.Add(new Paragraph("Normal", Disclaimer + " Paketteki listeler ve diyagramlar CRM'deki tanımlardan bir araçla "
            + "üretilmiştir; bir satır ya da kutu ile CRM'in kendisi çeliştiğinde CRM'deki tanım esas alınmalıdır.", "Uyarı: "));
        parts.Add(new Paragraph("Normal", "Sorularınızı ve fark ettiğiniz hataları Kurumsal Mimari ekibine iletebilirsiniz; "
            + "bildirdiğiniz düzeltmeler kılavuzun bir sonraki sürümüne yansıtılacaktır."));
        parts.Add(new Paragraph("Normal", "Kapsam, kurumun kendi kurduğu " + Number(inScope) + " süreçtir. "
            + "Okunan toplam " + Number(total) + " tanımın geri kalanı taslak, ürünle birlikte gelmiş ya da adı "
            + "deneme olup hiç çalışmamış tanımlardır. Bunlar pakete alınmadı; listeyi ayrıca süzmenize gerek yok."));
        parts.Add(new Paragraph("Heading2", "1.2  Kısa arka plan"));
        parts.Add(new Paragraph("Normal", "Bu CRM'de bir müşteri isteği bir \"talep\" olarak açılır. Talebin konusu (talep "
            + "konusu) hangi süreçten geçeceğini belirler: talep, o konunun başlangıç aşamasında başlar. Her \"talep aşaması\" "
            + "olumlu, olumsuz ya da iptal sonucuyla kapanır ve sonuca göre bir sonraki aşamaya geçer; bu sırada bir iş akışı "
            + "çalıştırabilir ve müşteriye SMS gönderebilir. Bu aşamalar CRM'in kendi \"İş Süreci Akışı\" çubuğu değildir: "
            + "kurumun kendi tanımladığı aşama kayıtlarıdır ve paketteki aşama haritaları bu kayıtlardan çizilmiştir. İş akışları "
            + "bu sürecin adımları olduğu için işe aşamalardan başlamak en kolay yoldur."));
        parts.Add(new Paragraph("Normal", "CRM'de \"iş akışı\", bir kayıt üzerinde bir olay gerçekleştiğinde sistemin "
            + "kendiliğinden yaptığı iştir: poliçe oluşturulduğunda bir alanı doldurmak, durum değiştiğinde e-posta "
            + "göndermek ya da bir onay beklemek gibi. İş akışları kod değil, ekrandan tanımlanmış kurallardır; bu nedenle yıllar "
            + "içinde sayıları artmıştır."));
        foreach ((string kind, string what) in new[]
        {
            ("İş Akışı", "arka planda ya da kayıt kaydedilirken çalışan asıl otomasyon. İşin büyük kısmı budur."),
            ("Diyalog", "kullanıcıya soru soran, adım adım ilerleyen ekran akışı."),
            ("İş Kuralı", "form üzerinde çalışan kural (alanı gizle, zorunlu yap). Tarayıcıda çalışır, iz bırakmaz."),
            ("Eylem", "başka iş akışlarının çağırdığı, girdi ve çıktısı olan yeniden kullanılabilir parça."),
            ("İş Süreci Akışı", "formun üstündeki, CRM'in kendi aşama çubuğu. Talep aşamalarından farklıdır ve bu pakette çizilmemiştir.")
        })
        {
            parts.Add(new Paragraph("NormalBullet", what, kind + ": "));
        }
        parts.Add(new Paragraph("Normal", "Belge boyunca iki ayrımla sık sık karşılaşacaksınız. Mod: \"arka plan\" iş akışı kullanıcıyı "
            + "bekletmeden, sonradan çalışır; \"gerçek zamanlı\" iş akışı kayıt kaydedilirken çalışır ve kullanıcıyı "
            + "bekletir. Durum: \"taslak\" bir tanım yeni çalıştırma başlatamaz, \"etkin\" bir tanım başlatabilir."));
        parts.Add(new Paragraph("Normal", "Bilgiler CRM'den yalnızca okunarak alındı; CRM'de hiçbir şey değiştirilmedi. "
            + "Her tanımın XAML metni okundu, adımları çözümlendi ve her biri için bir BPMN diyagramı üretildi. BPMN, "
            + "süreç çiziminin uluslararası standardıdır; .bpmn dosyalarını bpmn.io sitesinde veya Camunda Modeler'da açabilirsiniz."));
    }

    private static void Files(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "2  Dosyalar ve açılış sırası"));
        parts.Add(new Paragraph("Normal", "Dosyaları aşağıdaki sırayla açmanız işinizi kolaylaştıracaktır: her dosya, bir "
            + "sonrakinde neye bakacağınızı gösterir. Her çalışma kitabı, sütunları tek tek açıklayan bir \"Nasıl okunur\" "
            + "sayfasıyla başlar; sekmeler de burada anlatılan sırayla dizilmiştir."));
        parts.Add(new Table(
        [
            new DocumentRow(["Dosya", "Ne işe yarar"], Header: true),
            new DocumentRow(["tasima-plani.xlsx", "Taşınacak işin listesi: talep konuları, talep aşamaları, iş akışları. Çalışmaya buradan başlayabilirsiniz."]),
            new DocumentRow(["bpmn/talep-asamalari/", "Her talep konusunun aşama haritası: aşamalar, sonuçlar ve yolda çalışan iş akışları, iç içe."]),
            new DocumentRow(["bpmn/is-akislari/", "Plandaki her iş akışının kendi diyagramı, kategori ve kayıt türü klasörlerine ayrılmış."]),
            new DocumentRow(["veri-analizi.xlsx", "Hangi iş akışı hangi veriye dokunuyor, hangisi hangisini tetikliyor."]),
            new DocumentRow(["dis-sistemler.xlsx", "CRM dışına uzanan çağrılar, yani entegrasyon yükü."])
        ], [2800, 6800]));

        parts.Add(new Paragraph("Heading2", "2.1  tasima-plani.xlsx — iş listesi"));
        parts.Add(new Paragraph("Normal", "Taşınacak işin kendisi bu dosyadadır. Kurumun kurmadığı, taslak olan ve hiç "
            + "çalışmamış deneme iş akışları bu dosyaya alınmamıştır; listeyi ayrıca süzmenize gerek yoktur."));
        parts.Add(new Paragraph("NormalBullet", "her aşama haritası için bir satır: konunun adı, başladığı aşama, kaç aşama "
            + "ve kaç iş akışı taşıdığı ve açılacak dosya. Hangi süreçten başlayacağınızı bu sayfadan seçebilirsiniz.", SheetNames.Topics + " — "));
        parts.Add(new Paragraph("NormalBullet", "her etkin talep aşaması için bir satır: her sonuçta talebin geçtiği aşama, "
            + "çalışan iş akışı ve gönderilen SMS; aşamanın SLA, atama ve doküman kuralları. haritadaki_ad, aşamanın haritadaki "
            + "kutusunda yazan metindir.", SheetNames.Stages + " — "));
        parts.Add(new Paragraph("NormalBullet", "her iş akışı için bir satır. calistiran_asamalar doluysa iş akışı bir aşamanın "
            + "adımıdır ve o aşamanın haritasıyla birlikte tasarlanır; cagiran_is_akislari doluysa başka iş akışlarının parçasıdır.",
            SheetNames.Plan + " — "));
        parts.Add(new Paragraph("NormalBullet", "birbirini çağıran iş akışları, bir üst iş akışı altında: bir taşıma kaleminin "
            + "gerçek sınırı budur.", SheetNames.Trees + " — "));

        parts.Add(new Paragraph("Heading2", "2.2  bpmn/ — diyagramlar ve gösterim"));
        parts.Add(new Paragraph("Normal", "bpmn/talep-asamalari/ altında her talep konusu için bir aşama haritası vardır: "
            + "başlangıç aşamasından başlar ve talebin ulaşabileceği her aşamayı gösterir. Olumlu sonuç bir elmastan sağa, "
            + "olumsuz aşağıdan, iptal yukarıdan çıkar; aynı yere giden sonuçlar tek oktur (\"olumsuz / iptal\"). Sonuç "
            + "üzerindeki kutu o sonuçta çalışan iş akışıdır ve içine girildiğinde o iş akışının kendi adımları görünür. "
            + "Kullandığınız görüntüleyici kutunun içine girmeyi desteklemiyorsa, aynı iş akışının kendi diyagramını bpmn/is-akislari/ "
            + "altında bulabilirsiniz; dosyanın yolu kutunun açıklamasında yazılıdır."));
        parts.Add(new Paragraph("Normal", "bpmn/is-akislari/ altındaki bir iş akışı diyagramını bpmn.io ya da Camunda Modeler ile "
            + "açtığınızda önce üstteki not kutusuna göz atın: künye, iş akışını çalıştıran aşamalar, çağıranlar, kullanım "
            + "ve uyarılar oradadır. Ardından akışı soldan sağa izleyebilirsiniz; elmasların üzerindeki metin CRM'deki koşulun "
            + "kendisidir. Koşulun solundaki ad çoğunlukla bir alandır (varlik.alan); bir özel etkinliğin döndürdüğü değer "
            + "karşılaştırılıyorsa o etkinliğin adı ve çıktısı yazılır (Etkinlik.Cikti)."));
        parts.Add(new Table(
        [
            new DocumentRow(["Şekil", "Bu pakette anlamı"], Header: true),
            new DocumentRow(["Kullanıcı görevi (insan simgeli kutu)", "Bir kişinin tamamladığı talep aşaması."]),
            new DocumentRow(["Servis görevi (dişli simgeli kutu)", "Kendiliğinden ilerleyen aşama ya da bir iş akışında sistemin yaptığı adım."]),
            new DocumentRow(["Elmas (özel geçit)", "Karar: aşamada sonuca göre, iş akışında koşula göre ayrılan yol."]),
            new DocumentRow(["+ işaretli kutu (alt süreç)", "Haritada çalışan iş akışı; içine girilerek adımları görülür."]),
            new DocumentRow(["Kalın kenarlı kutu (çağrı etkinliği)", "Aynı iş akışı; adımları haritada ilk geçtiği kutunun içindedir."]),
            new DocumentRow(["Ok uçlu daire çifti (bağlantı olayı)", "\"… aşamasına dön\" ile biten akış, aynı adlı \"… aşamasına dönüş\" olayından devam eder."]),
            new DocumentRow(["Zarf simgeli daire (mesaj olayı)", "Müşteriye SMS gönderilir; metni olayın açıklamasındadır."]),
            new DocumentRow(["Kalın daire (bitiş olayı)", "Haritada yol biter: \"Sonraki aşama yok\" talebin kapandığını söylemez, aşama kaydı devamını göstermez."])
        ], [3600, 6000]));

        parts.Add(new Paragraph("Heading2", "2.3  veri-analizi.xlsx — iş akışları arasındaki veri bağları"));
        parts.Add(new Paragraph("NormalBullet", "her alan için kaç iş akışının yazdığı ve kaç iş akışının okuduğu. yazan > 1 olan "
            + "alanlarda yazma sırası CRM'de hiçbir zaman garanti edilmemiştir; yeni üründe bir sıranın kararlaştırılması gerekir.",
            "Veri ayak izi — "));
        parts.Add(new Paragraph("NormalBullet", "bir iş akışının yazdığı alan nedeniyle kendiliğinden başlayan diğer iş akışları. Bu bağ "
            + "diyagramlarda görünmez; yalnızca bu sayfada yer alır.", "Tetikleme zincirleri — "));

        parts.Add(new Paragraph("Heading2", "2.4  dis-sistemler.xlsx — entegrasyon yükü"));
        parts.Add(new Paragraph("Normal", "Bu dosya CRM dışına uzanan çağrıları listeler. Buradaki her etkinlik, yeni üründe ayrı bir "
            + "iş kalemi olarak ele alınmalıdır."));
        parts.Add(new Paragraph("NormalBullet", "çağrılan kod, onu çağıran iş akışları, çağrının parametre adları ve "
            + "derlemedeki_adresler (kodun içinde yazılı adresler). kayitli sütunu \"hayır\" ise iş akışı artık var olmayan "
            + "bir koda başvurmaktadır.", "Dış bağımlılıklar — "));
        parts.Add(new Paragraph("NormalBullet", "tanımın metnine yazılmış adresler, sunucularıyla birlikte. Bu sayfanın boş olması "
            + "\"hiçbir yere bağlanmıyor\" anlamına gelmez.", "Adresler — "));
        parts.Add(new Paragraph("NormalBullet", "iş akışı olmayan, bir mesaj üzerinde çalışan kod. Bu kodlar İş akışları sayfasında "
            + "görünmez, ancak yeni üründe karşılıklarının kurulması gerekir.", "Eklentiler — "));

        parts.Add(new Paragraph("Heading2", "2.5  Elinizdeki bilgiden diyagrama"));
        parts.Add(new Paragraph("Normal", "Elinizde bir ad, bir alan ya da bir adres olduğunda, ilgili diyagrama aşağıdaki yoldan ulaşabilirsiniz:"));
        parts.Add(new Table(
        [
            new DocumentRow(["Elinizdeki", "Açılacak dosya", "Aranacak sütun", "Ardından"], Header: true),
            new DocumentRow(["Aşama adı", "tasima-plani.xlsx → " + SheetNames.Stages, "asama sütunu", "haritadaki_ad metnini asama_haritasi dosyasında aratın"]),
            new DocumentRow(["İş akışı adı", "tasima-plani.xlsx → " + SheetNames.Plan, "is_akisi sütunu", "calistiran_asamalar doluysa önce haritayı, sonra bpmn_dosyasi'nı açın"]),
            new DocumentRow(["Alan ya da kayıt türü", "veri-analizi.xlsx → Veri ayak izi", "varlik · alan", "yazanlar sütunundaki iş akışlarını İş akışları sayfasında bulun"]),
            new DocumentRow(["Adres", "dis-sistemler.xlsx → Adresler", "adres · sunucu", "bpmn_dosyasi sütunundaki diyagramı açın"]),
            new DocumentRow(["SMS metni", "tasima-plani.xlsx → " + SheetNames.Stages, "*_sms sütunları", "aşamanın haritasında zarf simgeli olayı bulun"]),
            new DocumentRow(["Kimlik (GUID)", "ilgili .bpmn dosyası", "metin araması", "aşama ve iş akışı kimlikleri açıklamalarda yazılıdır"])
        ], [1900, 2700, 1800, 3200]));
    }

    /// <summary>
    /// A real map from this run, walked through as the analyst will walk every other: the subject with the most
    /// distinct workflows, because it shows the most of what a map can hold.
    /// </summary>
    private static void StageWalkthrough(List<DocumentPart> parts, RunState state)
    {
        parts.Add(new Paragraph("Heading1", "3  Bir talep konusunu adım adım çözümleme"));
        if (state.StageMapPaths.Count == 0)
        {
            parts.Add(new Paragraph("Normal", "Bu çalıştırmanın dışa aktarımı talep aşamalarını içermediği için aşama haritası "
                + "üretilmemiştir. Bir sonraki çalıştırmada bu bölüm gerçek bir harita üzerinde anlatılacaktır."));
            return;
        }
        Dictionary<Guid, CaseStage> byId = state.CaseStages.ToDictionary(stage => stage.Id);
        (Guid primary, string file, int stages, int workflows) = state.StageMapPaths
            .Select(entry =>
            {
                List<CaseStage> on = [.. state.StageMapFiles.Where(map => map.Value.Contains(entry.Value, StringComparer.Ordinal)).Select(map => byId[map.Key])];
                int fired = on.SelectMany(stage => stage.Exits).Where(exit => exit.Workflow is not null).Select(exit => exit.Workflow).Distinct().Count();
                return (entry.Key, entry.Value, on.Count, fired);
            })
            .OrderByDescending(map => map.Item4).ThenBy(map => map.Item2, StringComparer.Ordinal).First();
        CaseStage start = byId[primary];
        parts.Add(new Table(
        [
            new DocumentRow(["Örnek talep konusu", start.Process.Length > 0 ? start.Process : start.Name], Header: false),
            new DocumentRow(["Başlangıç aşaması", state.StageMapLabels.GetValueOrDefault(primary) ?? start.ShortName]),
            new DocumentRow(["Aşama haritası", file]),
            new DocumentRow(["Büyüklüğü", Number(stages) + " aşama · " + Number(workflows) + " farklı iş akışı"])
        ], [2600, 7000]));
        (string, string)[] steps =
        [
            ("Satırı bulun", "tasima-plani.xlsx → " + SheetNames.Topics + " sayfasını açın. Konunun satırında haritanın dosyası "
                + "ve haritanın kaç aşama ile kaç iş akışı içerdiği yazar."),
            ("Haritayı açıp notu okuyun", "Sol üstteki not; haritadaki aşama ve iş akışı sayısını, hangi iş akışının "
                + "kaç kez geçtiğini ve haritanın gösterim kurallarını açıklar."),
            ("Olumlu yolu izleyin", "\"Talep açılır\" olayından başlayıp her elmastan sağa çıkan olumlu oku takip edin. "
                + "Bu, her şey yolunda gittiğinde talebin geçtiği yoldur. Soru biçimindeki aşamalarda olumlu sonuç \"evet\" demektir."),
            ("Olumsuz ve iptal yollarını ekleyin", "Ardından aşağıdan çıkan olumsuz ve yukarıdan çıkan iptal oklarına geçin. \"… aşamasına "
                + "dön\" olayı talebin geri döndüğü yeri gösterir; akış, aynı adı taşıyan \"… aşamasına dönüş\" olayından devam eder."),
            ("Aşamanın kurallarına bakın", SheetNames.Stages + " sayfasındaki aşama satırında SLA, atama, doküman ve diğer kurallar "
                + "yer alır. Aynı bilgileri haritada aşamanın açıklamasında da bulabilirsiniz."),
            ("İş akışlarının içine girin", "+ işaretli bir kutunun içine girdiğinizde o sonuçta çalışan iş akışının adımları açılır. "
                + "Her iş akışını bir sonraki bölümde anlatıldığı gibi çözümleyebilirsiniz."),
            ("Süreci yeni üründe çizin", "Artık talep aşamaları, sonuçlar, SMS'ler ve iş akışları elinizde. Her birinin yeni üründeki "
                + "karşılığını yazın; karşılığı olmayanları ayrıca işaretlemeniz, sonraki çalışmayı kolaylaştıracaktır.")
        ];
        for (int index = 0; index < steps.Length; index++)
        {
            parts.Add(new Paragraph("Heading3", WordDocument.Invariant($"3.{index + 1}  {steps[index].Item1}")));
            parts.Add(new Paragraph("Normal", steps[index].Item2));
        }
    }

    private static void Walkthrough(List<DocumentPart> parts, RunState state, UsageEvidence? usage, WorkflowIr? example)
    {
        parts.Add(new Paragraph("Heading1", "4  Bir iş akışını adım adım çözümleme"));
        if (example is WorkflowIr sample)
        {
            WorkflowIdentity identity = sample.Identity;
            parts.Add(new Paragraph("Normal", "Aşağıdaki adımlar, planın ilk satırlarından biri üzerinde anlatılmaktadır. "
                + "Örnek iş akışı bu çalıştırmada gerçekten yer aldığından, dosyayı açarak adımları birlikte izleyebilirsiniz."));
            List<DocumentRow> rows =
            [
                new DocumentRow(["Örnek iş akışı", identity.Name], Header: false),
                new DocumentRow(["Künyesi", $"{identity.Category} · {identity.Mode} · {identity.State} · kayıt türü: {identity.PrimaryEntity ?? "—"}"]),
                new DocumentRow(["Diyagramı", state.BpmnFiles.GetValueOrDefault(identity.WorkflowId, "") + ".bpmn"])
            ];
            if (UsageStage.Verdict(identity, usage) is string verdict && verdict.Length > 0)
            {
                rows.Add(new DocumentRow(["Kullanım hükmü", verdict]));
            }
            parts.Add(new Table(rows, [2600, 7000]));
        }

        (string, string)[] steps =
        [
            ("Plandaki satırı okuyun", "tasima-plani.xlsx → " + SheetNames.Plan + " sayfasını açın. kategori, kayit_turu, "
                + "tetikleyici ve adim_sayisi sütunları iş akışının ne olduğunu anlatır. bekleme_var \"evet\" ise iş akışı zamana "
                + "yayılıyordur ve tasarımı da buna göre farklılaşır. kullanim sütunu iyi bir ipucudur, ancak tek başına karar "
                + "vermek için yeterli değildir."),
            ("İş akışının rolünü belirleyin", "Aynı satırdaki cagri_rolu \"aşama adımı\" ise iş akışını bir talep aşaması çalıştırır; "
                + "en doğrusu onu o aşamanın haritasıyla birlikte tasarlamaktır (calistiran_asamalar). \"yapı taşı\" ise başka iş "
                + "akışları onu çağırır ve tek başına taşınmaz (cagiran_is_akislari; " + SheetNames.Trees + " sayfası)."),
            ("Diyagramı açın", "bpmn_dosyasi sütunundaki dosyayı açın, üstteki not kutusunu okuyun ve akışı soldan sağa izleyin."),
            ("Tetikleyiciyi netleştirin", "İş akışını neyin başlattığını ve yeni üründe aynı olayın bir karşılığı olup olmadığını "
                + "belirleyin. tetikleyici, calistiran_asamalar ve calisma_modu sütunları ile diyagramın başlangıç olayı bu konuda "
                + "yol gösterir. Bu, tasarımın ilk kararıdır; cevabını yazdıktan sonra ilerlemeniz sonraki adımları kolaylaştırır."),
            ("Beklemeleri işaretleyin", "Diyagramdaki bekleme adımları süreci açık tutar. Her biri için \"ne kadar\" ve "
                + "\"neyi bekliyor\" sorularının cevabını not edin."),
            ("Veri bağlarını çıkarın", "yazdigi_varliklar sütunundaki varlıkları veri-analizi.xlsx → Veri ayak izi "
                + "sayfasında arayın. yazan > 1 olan her alan için bir yazma sırası kararı vermek gerekecektir."),
            ("Dış çağrıları ayırın", "ozel_etkinlikler sütunu doluysa, aynı etkinliği hangi iş akışlarının çağırdığını "
                + "dis-sistemler.xlsx → Dış bağımlılıklar sayfasında görebilirsiniz. Her biri ayrı bir iş kalemidir."),
            ("Çizimin ne kadar eksiksiz olduğuna bakın", "okunamayan_adim_sayisi 0 değilse diyagramda eksik adımlar vardır; "
                + "bu adımları CRM ekranından doğrulamanızı rica ederiz. calisan_kopya_farkli \"evet\" ise üretimde çalışan kopya, "
                + "çizilen tanımdan farklıdır."),
            ("Süreci çizin", "Artık tetikleyici, adımlar, kararlar, beklemeler ve dış çağrılar elinizde. Her birinin yeni üründeki "
                + "karşılığını yazın ve karşılığı olmayanları işaretleyin.")
        ];
        for (int index = 0; index < steps.Length; index++)
        {
            parts.Add(new Paragraph("Heading3", WordDocument.Invariant($"4.{index + 1}  {steps[index].Item1}")));
            parts.Add(new Paragraph("Normal", steps[index].Item2));
        }
    }

    private static void Traps(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "5  Dikkat edilmesi gereken noktalar"));
        foreach (string trap in new[]
        {
            "Planın kullanim sütunundaki \"kayıtlı çalışma yok\" ifadesi, iş akışının kullanılmadığını kanıtlamaz. CRM sistem "
                + "işlerini düzenli olarak siler, iş kuralları tarayıcıda çalışır ve iz bırakmaz, gerçek zamanlı iş akışları ise "
                + "yalnızca hataları kaydeder. Bu nedenle bu sütun tek başına bir silme gerekçesi olarak kullanılmamalıdır.",
            "Bir iş akışını yalnızca adına bakarak elemeyin. Adında DRAFT, TEST ya da ESKİ geçtiği halde üretimde her gün "
                + "çalışan iş akışları bulundu; bu nedenle ad, tek başına kapsam dışı bırakma gerekçesi sayılmadı.",
            "İş kuralları tarayıcıda çalışır ve kayıt bırakmaz; bu nedenle kullanımlarına ilişkin bir kanıt bulunmamaktadır.",
            "Gerçek zamanlı iş akışları kullanıcıyı bekletir. Yeni üründe aynı işin arka plana alınması, davranışı değiştirecektir.",
            "Tetikleme zincirleri diyagramda görünmez. Bir iş akışı, başka bir iş akışının izlediği alana yazdığı için onu "
                + "başlatıyor olabilir; bu bağ yalnızca veri-analizi.xlsx dosyasında yer alır.",
            "Diyagramdaki \"→ adres\", o adımın çalıştırdığı kodun içinde yazılı bir adrestir; adımın bu adrese bağlandığının "
                + "kanıtı değildir. Kod adresi parçalardan birleştiriyorsa adres hiçbir yerde görünmez.",
            "Bir aşama haritasındaki bitiş olayı, talebin kapandığı anlamına gelmez: \"Sonraki aşama yok\", aşama kaydının o sonuç "
                + "için bir devam tanımlamadığını gösterir. Talebin kapanıp kapanmadığı, aşamanın kapanış kurallarında yer alır.",
            "Bir aşama hem bir iş akışı çalıştırıp hem de bir sonraki aşamaya geçiyorsa, bu ikisinin sırası CRM'de doğrulanmamıştır; "
                + "haritada iş akışı önce çizilmiştir.",
            "Bir aşamanın olumlu ya da olumsuz kapanmasına iş akışı karar vermez; iş akışı, sonuç belli olduktan sonra çalışır. "
                + "Kararı ya bir kişi verir ya da CRM'e kaydedilmiş bir kod: dis-sistemler.xlsx → Eklentiler, talep_surecinde = evet.",
            "Diyagramdaki \"Çağrı ipucu\" (SOAP istemcisi, POST, text/xml…) derlemede o kelimenin geçtiğini gösterir; hangi "
                + "çağrının bunu kullandığını göstermez. Fiil ve gövdenin kesin hali kaynak kodda ya da servisin sözleşmesinde (WSDL) "
                + "yer alır; en güvenilir bilgiyi derlemenin sahibinden alabilirsiniz."
        })
        {
            parts.Add(new Paragraph("NormalBullet", trap));
        }
    }

    private static void Checklist(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "6  Tamamlamadan önce kontrol listesi"));
        parts.Add(new Paragraph("Normal", "Bir süreci tamamlanmış saymadan önce aşağıdaki maddeleri kısaca gözden geçirmeniz, "
            + "gözden kaçan bir noktayı erkenden yakalamanıza yardımcı olacaktır."));
        foreach (string item in new[]
        {
            "Aşama haritasındaki her sonuç izlenmiştir: olumlu, olumsuz, iptal ve geri dönüşler.",
            "Tetikleyici ve tetikleyen alanlar yazılmıştır; iş akışı bir aşama adımıysa onu çalıştıran aşamalar da yazılmıştır.",
            "Bütün dallar izlenmiştir: her karar noktasının her iki tarafı da çizimde yer almaktadır.",
            "Bekleme adımlarının ne kadar ve neyi beklediği not edilmiştir.",
            "Çağrılan alt iş akışları açılmış ve aynı taşıma kalemine bağlanmıştır.",
            "Yazılan alanlar çıkarılmış ve aynı alana yazan başka bir iş akışı olup olmadığı kontrol edilmiştir.",
            "Dış çağrılar ayrı birer iş kalemi olarak yazılmıştır.",
            "Sürecin kimin yetkisiyle çalıştığı (calisma_kimligi) ve elle başlatılıp başlatılamadığı (elle_baslatilabilir) yazılmıştır.",
            "Okunamayan adım kalmamıştır; kalan adımlar CRM ekranında doğrulanmıştır.",
            "Her adımın \"yeni üründe karşılığı\" satırı doldurulmuştur; karşılığı olmayanlar işaretlenmiştir."
        })
        {
            parts.Add(new Paragraph("NormalBullet", item));
        }
    }

    private static void Glossary(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "7  Sözlük"));
        parts.Add(new Table(
        [
            new DocumentRow(["Terim", "Anlamı"], Header: true),
            new DocumentRow(["Talep", "Bir müşteri isteği. CRM'de bir kayıt olarak açılır ve konusuna göre bir aşama sürecinden geçer."]),
            new DocumentRow(["Talep konusu", "Talebin alt kategorisi. Hangi aşamalardan geçeceğini belirler; her konunun bir aşama haritası vardır."]),
            new DocumentRow(["Talep aşaması", "Talebin geçtiği adım. Olumlu, olumsuz ya da iptal sonucuyla kapanır ve sonuca göre bir sonraki aşamaya geçer."]),
            new DocumentRow(["Başlangıç aşaması", "Bir konudaki taleplerin başladığı aşama; aşama haritası buradan çizilir."]),
            new DocumentRow(["Otomatik aşama", "Bir kişiyi beklemeden ilerleyen aşama; haritada servis görevi olarak çizilir."]),
            new DocumentRow(["Sonuç", "Bir aşamanın kapanış biçimi: olumlu, olumsuz ya da iptal."]),
            new DocumentRow(["Aşama haritası", "Bir talep konusunun bütün aşamalarını, sonuçlarını ve yolda çalışan iş akışlarını gösteren BPMN dosyası."]),
            new DocumentRow(["İş akışı", "CRM'in bir kayıt olayında, bir aşamanın sonucunda ya da başka bir iş akışının çağrısıyla kendiliğinden yaptığı iş."]),
            new DocumentRow(["Kayıt türü", "İş akışının üzerinde çalıştığı kayıt türü: poliçe, müşteri, talep gibi."]),
            new DocumentRow(["Tetikleyici", "İş akışını başlatan kayıt olayı: kayıt oluşturma, alan güncelleme, silme ya da kullanıcının isteği."]),
            new DocumentRow(["Aşama adımı", "Bir talep aşamasının sonucunda çalışan iş akışı. Aşama haritasıyla birlikte tasarlanır."]),
            new DocumentRow(["Alt iş akışı", "Başka bir iş akışının çağırdığı iş akışı. Tek başına değil, çağıranıyla birlikte taşınır."]),
            new DocumentRow(["Giriş noktası · yapı taşı", "Giriş noktası kendi başına başlayan bir iş akışıdır; yapı taşı başka iş akışlarınca çağrılır ve bir kez taşınır."]),
            new DocumentRow(["Çalışma modu", "Arka plan: kullanıcı beklemez. Gerçek zamanlı: kayıt kaydedilirken çalışır, kullanıcıyı bekletir."]),
            new DocumentRow(["Okunamayan adım", "Ayrıştırıcının anlamadığı adım; diyagramda OKUNAMADI olarak işaretlidir."]),
            new DocumentRow(["Özel etkinlik", "CRM'e kaydedilmiş, iş akışının çağırdığı derlenmiş kod. İş akışlarının CRM dışına ulaşabildiği tek yol budur."]),
            new DocumentRow(["Çalışan kopya", "CRM'in etkinleştirilen tanımdan çıkardığı ve gerçekte çalıştırdığı kopya. Tanımdan farklıysa (calisan_kopya_farkli) çalışan kopya geçerlidir."]),
            new DocumentRow(["BPMN", "Süreç çizmenin standardı. .bpmn dosyaları bpmn.io veya Camunda Modeler ile açılır."])
        ], [2600, 7000]));
    }

    /// <summary>
    /// The workflow the walkthrough is written on: a live process with a diagram, and as much of what the guide
    /// talks about as one workflow can carry — an outside call, and more than a handful of steps.
    /// </summary>
    private static WorkflowIr? Example(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        return MigrationPlan.InScope(state, documents, usage)
            .Where(document => MigrationPlan.IsLiveProcess(document) && state.BpmnFiles.ContainsKey(document.Identity.WorkflowId))
            .OrderByDescending(document => document.Dependencies.CustomActivities.Count > 0)
            .ThenByDescending(document => document.Steps.Count)
            .ThenBy(document => document.Identity.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// The logo on the cover: the one built into the tool, or the PNG the configuration names instead. A named
    /// file that cannot be read is said out loud and the guide goes out without a logo — it is a mark on a cover,
    /// not a reason to fail a run.
    /// </summary>
    private static byte[]? LogoBytes(string logoFile)
    {
        if (logoFile.Length == 0)
        {
            using Stream? built = typeof(AnalystGuide).Assembly.GetManifestResourceStream("Crm.Cli.Resources.kurumsal-logo.png");
            if (built is null)
            {
                return null;
            }
            MemoryStream copy = new();
            built.CopyTo(copy);
            return copy.ToArray();
        }
        string path = Path.IsPathRooted(logoFile) ? logoFile : Path.Combine(AppContext.BaseDirectory, logoFile);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static PngImage? Logo(RunState state, string logoFile)
    {
        byte[]? bytes = LogoBytes(logoFile);
        PngImage? picture = bytes is null ? null : PngImage.TryRead(bytes);
        if (picture is null && logoFile.Length > 0)
        {
            state.Warnings.Add($"Run:LogoFile olarak verilen '{logoFile}' okunamadı; kılavuz logosuz üretildi.");
        }
        return picture;
    }

    private static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The run stamp as a date a reader recognises; the folder name keeps the rest.</summary>
    private static string Stamp(string runId)
    {
        return DateTime.TryParseExact(runId, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at)
            ? at.ToString("d MMMM yyyy", new CultureInfo("tr-TR"))
            : runId;
    }
}

/// <summary>One row of the guide's revision table: its number, the day it was made and what changed.</summary>
internal sealed record Revision(string Number, string Date, string Change);
