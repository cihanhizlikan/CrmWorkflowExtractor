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

    public static byte[] Build(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage, string logoFile)
    {
        List<DocumentPart> parts = [];
        PngImage? logo = Logo(state, logoFile);
        byte[]? image = LogoBytes(state, logoFile);
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
        return WordDocument.Build(parts, image);
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
        parts.Add(new Paragraph("CoverDocInfo", state.ToolVersion, "Üretim Sürümü: "));
        parts.Add(new Paragraph("CoverDocInfo", state.RunId, "Çalıştırma: "));
        parts.Add(new Paragraph("Normal", ""));
        parts.Add(new Paragraph("Normal", "Bu belge, kurumun CRM sisteminde çalışan " + Number(documents.Count)
            + " süreç tanımının okunmuş ve çizilmiş halini anlatır. Bunların " + Number(inScope)
            + " tanesi yeni üründe yeniden kurulacak iştir; taleplerin geçtiği aşamalar " + Number(state.StageMapPaths.Count)
            + " aşama haritasında çizilmiştir. CRM'i hiç görmemiş ama BPMN okuyan bir sistem analisti bu belgeyi baştan "
            + "sona okuyup ilk süreci aynı gün çözümleyebilir."));
        parts.Add(new Table(
        [
            new DocumentRow(["Revizyon Tarihi", "Yazan", "Revizyon No", "Revizyon Açıklaması"], Header: true),
            new DocumentRow([Stamp(state.RunId), "Kurumsal Mimari", state.ToolVersion, "Çalıştırma " + state.RunId + " için üretildi."])
        ], [2200, 2200, 1800, 3400]));
        parts.Add(new Paragraph("Normal", "Bu paketteki her şey üretim verisinden üretildi ve kurum dışına "
            + "çıkarılmamalıdır. Diyagramlar açıklayıcıdır: anlamak ve yeniden tasarlamak içindir, "
            + "çalıştırılamazlar ve CRM'e geri yüklenemezler."));
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
            "1  Giriş", "    1.1  Amaç ve kapsam", "    1.2  Beş dakikada arka plan",
            "2  Dosyalar ve açılış sırası", "    2.1  tasima-plani.xlsx", "    2.2  bpmn/ — diyagramlar ve gösterim",
            "    2.3  veri-analizi.xlsx", "    2.4  dis-sistemler.xlsx", "    2.5  Elinizdekinden diyagrama",
            "3  Bir talep konusunu adım adım çözümleme", "4  Bir iş akışını adım adım çözümleme",
            "5  Nelere dikkat edeceksiniz", "6  Bitirmeden önce kontrol listesi", "7  Sözlük"
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
        parts.Add(new Paragraph("Normal", "Bu paket, CRM'deki süreçlerin yeni bir ürüne taşınabilmesi için "
            + "hazırlandı. İçinde taşınacak işin listesi, her sürecin çizilmiş hali ve bu süreçlerin veriye ve "
            + "dış sistemlere nasıl dokunduğu vardır. Amaç, bir sistem analistinin CRM ekranlarını açmadan "
            + "sürecin ne yaptığını anlaması ve yeni tasarımı buradan çıkarabilmesidir."));
        parts.Add(new Paragraph("Normal", "Kapsam, kurumun kendi kurduğu " + Number(inScope) + " süreçtir. "
            + "Okunan toplam " + Number(total) + " tanımın geri kalanı taslaktır, ürünle gelmiştir ya da adı "
            + "deneme olup hiç çalışmamıştır; bunlar pakette yoktur, dolayısıyla listeyi süzmeniz gerekmez."));
        parts.Add(new Paragraph("Heading2", "1.2  Beş dakikada arka plan"));
        parts.Add(new Paragraph("Normal", "Bu CRM'de bir müşteri isteği bir TALEP olarak açılır. Talebin konusu (talep "
            + "konusu) hangi süreçten geçeceğini belirler: talep, o konunun başlangıç aşamasında başlar. Her TALEP AŞAMASI "
            + "olumlu, olumsuz ya da iptal sonucuyla kapanır ve sonuca göre bir sonraki aşamaya geçer; yolda bir iş akışı "
            + "çalıştırabilir ve müşteriye SMS gönderebilir. Bu aşamalar CRM'in kendi \"İş Süreci Akışı\" çubuğu değildir: "
            + "kurumun kendi tanımladığı aşama kayıtlarıdır ve paketteki aşama haritaları onlardan çizilmiştir. Önce aşamaları "
            + "anlayın: iş akışları bu sürecin adımlarıdır."));
        parts.Add(new Paragraph("Normal", "CRM'de \"iş akışı\", bir kayıt üzerinde bir şey olduğunda sistemin "
            + "kendiliğinden yaptığı iştir: poliçe oluşturulunca bir alan doldurmak, durum değişince e-posta "
            + "göndermek, bir onay beklemek gibi. Kod değil, ekrandan tanımlanmış kurallardır; bu yüzden yıllar "
            + "içinde çoğalmışlardır."));
        foreach ((string kind, string what) in new[]
        {
            ("İş Akışı", "arka planda ya da kayıt kaydedilirken çalışan asıl otomasyon. İşin büyük kısmı budur."),
            ("Diyalog", "kullanıcıya soru soran, adım adım ilerleyen ekran akışı."),
            ("İş Kuralı", "form üzerinde çalışan kural (alanı gizle, zorunlu yap). Tarayıcıda çalışır, iz bırakmaz."),
            ("Eylem", "başka akışların çağırdığı, girdi ve çıktısı olan yeniden kullanılabilir parça."),
            ("İş Süreci Akışı", "formun üstündeki CRM'in kendi aşama çubuğu. Talep aşamalarıyla karıştırmayın; bu pakette çizilmez.")
        })
        {
            parts.Add(new Paragraph("NormalBullet", what, kind + ": "));
        }
        parts.Add(new Paragraph("Normal", "İki ayrım her yerde karşınıza çıkar. Mod: \"arka plan\" akış kullanıcı "
            + "beklemeden sonra çalışır, \"gerçek zamanlı\" akış kayıt kaydedilirken çalışır ve kullanıcıyı "
            + "bekletir. Durum: \"taslak\" bir tanım yeni çalıştırma başlatamaz, \"etkin\" olan başlatır."));
        parts.Add(new Paragraph("Normal", "Araç CRM'e yalnızca okuma amacıyla bağlandı; hiçbir şeyi değiştirmedi. "
            + "Her tanımın kendi XAML metnini aldı, adımlarını çözümledi ve her biri için bir BPMN diyagramı "
            + "üretti. BPMN, süreçleri çizmenin uluslararası standardıdır: .bpmn dosyalarını bpmn.io sitesinde "
            + "veya Camunda Modeler'da açabilirsiniz."));
    }

    private static void Files(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "2  Dosyalar ve açılış sırası"));
        parts.Add(new Paragraph("Normal", "Sıra önemlidir: her adım bir sonrakinde ne arayacağınızı söyler. Her "
            + "çalışma kitabının ilk sayfası \"Nasıl okunur\"dur ve sütunların tek tek ne işe yaradığını yazar; "
            + "sekmeler de burada anlatılan sırayla dizilidir."));
        parts.Add(new Table(
        [
            new DocumentRow(["Dosya", "Ne işe yarar"], Header: true),
            new DocumentRow(["tasima-plani.xlsx", "Taşınacak işin listesi: talep konuları, talep aşamaları, iş akışları. Buradan başlarsınız."]),
            new DocumentRow(["bpmn/talep-asamalari/", "Her talep konusunun aşama haritası: aşamalar, sonuçlar ve yolda çalışan iş akışları, iç içe."]),
            new DocumentRow(["bpmn/is-akislari/", "Plandaki her iş akışının kendi diyagramı, kategori ve kayıt türü klasörlerine ayrılmış."]),
            new DocumentRow(["veri-analizi.xlsx", "Hangi akış hangi veriye dokunuyor, hangisi hangisini tetikliyor."]),
            new DocumentRow(["dis-sistemler.xlsx", "CRM dışına uzanan çağrılar: entegrasyon yükü."])
        ], [2800, 6800]));

        parts.Add(new Paragraph("Heading2", "2.1  tasima-plani.xlsx — sizin iş listeniz"));
        parts.Add(new Paragraph("Normal", "İşin kendisi. Süzmeniz gerekmez: kurumun kurmadığı, taslak olan ve hiç "
            + "çalışmamış deneme akışları bu dosyada zaten yoktur."));
        parts.Add(new Paragraph("NormalBullet", "her aşama haritası için bir satır: konunun adı, başladığı aşama, kaç aşama "
            + "ve kaç iş akışı taşıdığı, açılacak dosya. Hangi süreçten başlayacağınızı buradan seçin.", SheetNames.Topics + " — "));
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
            + "Görüntüleyiciniz kutunun içine girmeyi desteklemiyorsa aynı iş akışının kendi diyagramı bpmn/is-akislari/ altındadır; "
            + "yolu kutunun açıklamasında yazar."));
        parts.Add(new Paragraph("Normal", "bpmn/is-akislari/ altındaki bir iş akışı diyagramını bpmn.io ya da Camunda Modeler ile "
            + "açın. Üstteki not kutusunu okuyun: künye, onu çalıştıran aşamalar, çağıranlar, kullanım ve uyarılar oradadır. Sonra "
            + "akışı soldan sağa izleyin; elmasların üzerindeki metin CRM'deki koşulun kendisidir. Solundaki ad çoğunlukla bir "
            + "alandır (varlik.alan); bir özel etkinliğin döndürdüğü değer karşılaştırılıyorsa o etkinliğin adı ve çıktısı yazar "
            + "(Etkinlik.Cikti)."));
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

        parts.Add(new Paragraph("Heading2", "2.3  veri-analizi.xlsx — bir akışı tek başına tasarlayamayacağınız yerler"));
        parts.Add(new Paragraph("NormalBullet", "alan alan: kaç akış yazıyor, kaç akış okuyor. yazan > 1 olan alanlarda "
            + "sıra CRM'de hiçbir zaman garanti edilmedi; yeni üründe bir sıra kararlaştırın.", "Veri ayak izi — "));
        parts.Add(new Paragraph("NormalBullet", "bir akışın yazmasıyla kendiliğinden başlayan başka akışlar. Bu bağ "
            + "diyagramlarda görünmez; yalnızca burada vardır.", "Tetikleme zincirleri — "));

        parts.Add(new Paragraph("Heading2", "2.4  dis-sistemler.xlsx — entegrasyon yükü"));
        parts.Add(new Paragraph("Normal", "CRM dışına uzanan çağrılar. Buradaki her etkinlik, yeni üründe ayrı bir "
            + "iş kalemidir."));
        parts.Add(new Paragraph("NormalBullet", "çağrılan kod, onu çağıran akışlar, çağrının parametre adları ve "
            + "derlemedeki_adresler: kodun içinde yazılı adresler. kayitli sütunu hayır ise akış artık var olmayan "
            + "bir koda gidiyor demektir.", "Dış bağımlılıklar — "));
        parts.Add(new Paragraph("NormalBullet", "tanımın metnine yazılmış adresler, sunucusuyla birlikte. Boş olması "
            + "\"hiçbir yere bağlanmıyor\" demek değildir.", "Adresler — "));
        parts.Add(new Paragraph("NormalBullet", "iş akışı OLMAYAN, bir mesaj üzerinde çalışan kod. İş akışları sayfasında hiç "
            + "görünmezler ama yeni üründe karşılıkları kurulmalıdır.", "Eklentiler — "));

        parts.Add(new Paragraph("Heading2", "2.5  Elinizdekinden diyagrama"));
        parts.Add(new Paragraph("Normal", "Elinizde bir ad, bir alan ya da bir adres varsa onu hangi diyagramda bulacağınız:"));
        parts.Add(new Table(
        [
            new DocumentRow(["Elinizde", "Açın", "Arayın", "Sonra"], Header: true),
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
            parts.Add(new Paragraph("Normal", "Bu çalıştırmanın dışa aktarımı talep aşamalarını taşımıyor: aşama haritası yok. "
                + "Bir sonraki çalıştırmada bu bölüm gerçek bir harita üzerinde anlatılır."));
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
            ("Satırı bulun", "tasima-plani.xlsx → " + SheetNames.Topics + ". Konunun satırı haritanın dosyasını, kaç aşama ve kaç "
                + "iş akışı taşıdığını söyler."),
            ("Haritayı açın ve notu okuyun", "Sol üstteki not, haritadaki aşama ve iş akışı sayısını, hangi iş akışının kaç "
                + "kez geçtiğini ve haritanın gösterim kurallarını yazar."),
            ("Olumlu yolu izleyin", "\"Talep açılır\" olayından başlayıp her elmastan sağa çıkan olumlu okları izleyin: "
                + "talebin her şey yolunda gittiğinde geçtiği yol budur. Soru biçimindeki aşamalarda olumlu = evet."),
            ("Olumsuz ve iptal yollarını ekleyin", "Aşağıdan çıkan olumsuz, yukarıdan çıkan iptal oklarını izleyin. \"… aşamasına "
                + "dön\" olayı talebin geri döndüğü yerdir; aynı adlı \"… aşamasına dönüş\" olayını bulun."),
            ("Aşamanın kurallarını okuyun", SheetNames.Stages + " sayfasında aşamanın satırı: SLA, atama, doküman ve diğer kurallar. "
                + "Haritada aşamanın açıklamasında da yazılıdır."),
            ("İş akışlarına girin", "+ işaretli kutunun içine girin: o sonuçta çalışan iş akışının adımları oradadır. Her "
                + "iş akışını bir sonraki bölümdeki gibi çözümleyin."),
            ("Süreci yeni üründe çizin", "Talep aşamaları, sonuçlar, SMS'ler ve iş akışları elinizde. Yeni üründeki karşılıklarını "
                + "yazın; karşılığı olmayanları işaretleyin.")
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
            parts.Add(new Paragraph("Normal", "Aşağıdaki sıra, planın ilk satırlarından biri üzerinde anlatılıyor. "
                + "Bu akış bu çalıştırmada gerçekten var; dosyayı açıp birlikte ilerleyebilirsiniz."));
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
            ("Planda satırı okuyun", "tasima-plani.xlsx → " + SheetNames.Plan + ". kategori, kayit_turu, tetikleyici ve adim_sayisi "
                + "sütunları iş akışının ne olduğunu söyler; bekleme_var evet ise süreç zamana yayılıyor demektir ve tasarımı "
                + "baştan farklıdır. kullanim sütununa bakın ama tek başına karar vermeyin."),
            ("Tek başına mı, parça mı", "Aynı satırda cagri_rolu: \"aşama adımı\" ise bir talep aşaması çalıştırır, haritasıyla "
                + "birlikte tasarlayın (calistiran_asamalar). \"yapı taşı\" ise başka iş akışları bunu çağırıyor, tek başına "
                + "taşınmaz (cagiran_is_akislari; " + SheetNames.Trees + " sayfası)."),
            ("Diyagramı açın", "bpmn_dosyasi sütunundaki dosyayı açın, üstteki not kutusunu okuyun, sonra akışı soldan "
                + "sağa izleyin."),
            ("Tetikleyiciyi karara bağlayın", "Akışı ne başlatıyor ve yeni üründe aynı olayın karşılığı var mı? "
                + "tetikleyici, calistiran_asamalar ve calisma_modu sütunları ile diyagramın başlangıç olayı bunu söyler. "
                + "Bu tasarımın ilk kararıdır; cevabını yazmadan devam etmeyin."),
            ("Beklemeleri işaretleyin", "Diyagramdaki bekleme adımları süreci açık tutar. Her biri için \"ne kadar\" ve "
                + "\"neyi bekliyor\" sorularını cevaplayın."),
            ("Veri bağlarını çıkarın", "yazdigi_varliklar sütunundaki varlıkları veri-analizi.xlsx → Veri ayak izi "
                + "sayfasında aratın. yazan > 1 olan her alan bir sıra kararıdır."),
            ("Dış çağrıları ayırın", "ozel_etkinlikler sütunu doluysa dis-sistemler.xlsx → Dış bağımlılıklar sayfasından "
                + "aynı etkinliği kimlerin çağırdığına bakın. Her biri ayrı bir iş kalemidir."),
            ("Çizime ne kadar güveneceğinizi bilin", "okunamayan_adim_sayisi 0 değilse diyagram eksiktir; o adımları CRM "
                + "ekranından doğrulayın. calisan_kopya_farkli evet ise üretimde çalışan kopya çizilenden farklıdır."),
            ("Süreci çizin", "Tetikleyici, adımlar, kararlar, beklemeler ve dış çağrılar elinizde. Yeni üründeki "
                + "karşılıklarını yazın; karşılığı olmayanları işaretleyin.")
        ];
        for (int index = 0; index < steps.Length; index++)
        {
            parts.Add(new Paragraph("Heading3", WordDocument.Invariant($"4.{index + 1}  {steps[index].Item1}")));
            parts.Add(new Paragraph("Normal", steps[index].Item2));
        }
    }

    private static void Traps(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "5  Nelere dikkat edeceksiniz"));
        foreach (string trap in new[]
        {
            "Planın kullanim sütunundaki \"kayıtlı çalışma yok\", kullanılmadığını KANITLAMAZ. CRM sistem işlerini "
                + "düzenli olarak siler, iş kuralları tarayıcıda çalışıp hiç iz bırakmaz, gerçek zamanlı akışlar "
                + "yalnızca hatayı kaydeder. Bu sütun bir silme gerekçesi değildir.",
            "Ada bakarak temizlik yapmayın. Adında DRAFT, TEST ya da ESKİ geçen ve üretimde her gün çalışan akışlar "
                + "bulundu; bu yüzden ad tek başına kapsam dışı bırakma gerekçesi sayılmadı.",
            "İş kuralları tarayıcıda çalışır ve hiçbir kayıt bırakmaz: kullanımları hakkında hiçbir kanıt yoktur.",
            "Gerçek zamanlı akışlar kullanıcıyı bekletir. Yeni üründe aynı işi arka plana almak davranışı değiştirir.",
            "Tetikleme zincirleri diyagramda görünmez. Bir akış, başka bir akışın izlediği alana yazdığı için onu "
                + "başlatıyor olabilir; bu bağ yalnızca veri-analizi.xlsx'tedir.",
            "Diyagramdaki \"→ adres\", o adımın çalıştırdığı KODUN İÇİNDE yazılı bir adrestir; adımın oraya gittiğinin "
                + "kanıtı değildir. Kod adresi parçalardan birleştiriyorsa hiçbir yerde görünmez.",
            "Bir aşama haritasındaki bitiş olayı talebin kapandığını söylemez: \"Sonraki aşama yok\", aşama kaydının o sonuç "
                + "için bir devam göstermediği demektir. Talebin kapanıp kapanmadığı aşamanın kapanış kurallarındadır.",
            "Bir aşama hem bir iş akışı çalıştırıp hem bir sonraki aşamaya geçiyorsa, ikisinin sırası CRM'de doğrulanmadı; "
                + "haritada iş akışı önce çizilir.",
            "Bir aşamanın olumlu mu olumsuz mu kapandığına iş akışı karar vermez; iş akışı sonuç belli olduktan sonra çalışır. "
                + "Karar ya kişinindir ya da CRM'e kaydedilmiş koddadır: dis-sistemler.xlsx → Eklentiler, talep_surecinde = evet.",
            "Diyagramdaki \"Çağrı ipucu\" (SOAP istemcisi, POST, text/xml…) derlemede o kelimenin geçtiğini söyler; hangi "
                + "çağrının kullandığını söylemez. Fiil ve gövdenin kesin hali kaynak kodda ya da servisin sözleşmesindedir (WSDL)."
        })
        {
            parts.Add(new Paragraph("NormalBullet", trap));
        }
    }

    private static void Checklist(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "6  Bitirmeden önce kontrol listesi"));
        foreach (string item in new[]
        {
            "Aşama haritasındaki her sonucu izlediniz: olumlu, olumsuz, iptal ve geri dönüşler.",
            "Tetikleyiciyi ve tetikleyen alanları yazdınız; aşama adımıysa çalıştıran aşamaları.",
            "Bütün dalları izlediniz: her karar noktasının iki tarafı da çizimde var.",
            "Bekleme adımlarının ne kadar beklediğini ve neyi beklediğini not ettiniz.",
            "Çağrılan alt iş akışlarını açtınız ve aynı taşıma kalemine bağladınız.",
            "Yazdığı alanları çıkardınız ve aynı alana yazan başka iş akışı olup olmadığına baktınız.",
            "Dış çağrıları ayrı bir iş kalemi olarak yazdınız.",
            "Sürecin kimin yetkisiyle çalıştığını (calisma_kimligi) ve elle başlatılıp başlatılmadığını (elle_baslatilabilir) yazdınız.",
            "Okunamayan adım kalmadı; kalanları CRM ekranında doğruladınız.",
            "Her adımın \"yeni üründe karşılığı\" satırı dolu; karşılığı olmayanlar işaretli."
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
            new DocumentRow(["Giriş noktası · yapı taşı", "Giriş noktası tek başına bir süreçtir; yapı taşı başka iş akışlarınca çağrılır ve bir kez taşınır."]),
            new DocumentRow(["Çalışma modu", "Arka plan: kullanıcı beklemez. Gerçek zamanlı: kayıt kaydedilirken çalışır, kullanıcıyı bekletir."]),
            new DocumentRow(["Okunamayan adım", "Ayrıştırıcının anlamadığı adım; diyagramda OKUNAMADI olarak işaretlidir."]),
            new DocumentRow(["Özel etkinlik", "CRM'e kaydedilmiş, iş akışının çağırdığı derlenmiş kod. Dışarıya açılan tek kapı budur."]),
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
    private static byte[]? LogoBytes(RunState state, string logoFile)
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
        byte[]? bytes = LogoBytes(state, logoFile);
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
