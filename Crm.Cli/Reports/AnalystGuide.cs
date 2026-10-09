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
/// It says nothing about combined models or families: the first delivery is the original diagrams only
/// (maintainer, 2026-10-02), and a guide that named what the reader was not given would send them looking for it.
/// </para>
/// </summary>
public static class AnalystGuide
{
    private const string Title = "CRM İş Akışları — Sistem Analisti Kılavuzu";

    public static byte[] Build(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage, string logoFile)
    {
        List<DocumentPart> parts = [];
        PngImage? logo = Logo(state, logoFile);
        byte[]? image = LogoBytes(state, logoFile);
        int inScope = MigrationPlan.InScope(documents, usage).Count();

        Cover(parts, state, documents, inScope, logo);
        Contents(parts);
        Introduction(parts, documents.Count, inScope);
        Files(parts);
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
        parts.Add(new Paragraph("CoverDocInfo", "Süreç Analizi ve Taşıma Kılavuzu", "Doküman Adı: "));
        parts.Add(new Paragraph("CoverDocInfo", Stamp(state.RunId), "Yayın Tarihi: "));
        parts.Add(new Paragraph("CoverDocInfo", state.ToolVersion, "Üretim Sürümü: "));
        parts.Add(new Paragraph("CoverDocInfo", state.RunId, "Çalıştırma: "));
        parts.Add(new Paragraph("Normal", ""));
        parts.Add(new Paragraph("Normal", "Bu belge, kurumun CRM sisteminde çalışan " + Number(documents.Count)
            + " süreç tanımının okunmuş ve çizilmiş halini anlatır. Bunların " + Number(inScope)
            + " tanesi yeni üründe yeniden kurulacak iştir. CRM'i hiç görmemiş bir sistem analisti de bu belgeyi "
            + "baştan sona okuyup ilk iş akışını aynı gün çözümleyebilir."));
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
            "2  Dosyalar ve açılış sırası", "3  Bir iş akışını adım adım çözümleme",
            "4  Nelere dikkat edeceksiniz", "5  Bitirmeden önce kontrol listesi", "6  Sözlük"
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
            ("İş Süreci Akışı", "formun üstündeki aşama çubuğu; bu araç onların içini henüz okuyamıyor.")
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
            new DocumentRow(["tasima-plani.xlsx", "Taşınacak işin listesi: önce talebin geçtiği aşamalar, sonra iş akışları. Buradan başlarsınız."]),
            new DocumentRow(["bpmn/asama-akislari/", "Her talep konusunun aşama akışı: aşamalar, sonuçlar ve yolda çalışan iş akışları, iç içe."]),
            new DocumentRow(["bpmn/", "Her iş akışının diyagramı, kategori ve varlık klasörlerine ayrılmış."]),
            new DocumentRow(["veri-analizi.xlsx", "Hangi akış hangi veriye dokunuyor, hangisi hangisini tetikliyor."]),
            new DocumentRow(["dis-sistemler.xlsx", "CRM dışına uzanan çağrılar: entegrasyon yükü."])
        ], [2800, 6800]));

        parts.Add(new Paragraph("Heading2", "2.1  tasima-plani.xlsx — sizin iş listeniz"));
        parts.Add(new Paragraph("Normal", "İşin kendisi. Süzmeniz gerekmez: kurumun kurmadığı, taslak olan ve hiç "
            + "çalışmamış deneme akışları bu dosyada zaten yoktur."));
        parts.Add(new Paragraph("NormalBullet", "talebin geçtiği her etkin aşama için bir satır. Bir talep, konusunun "
            + "birincil aşamasında başlar; her aşama olumlu, olumsuz ya da iptal sonucuyla kapanır, sonuca göre bir sonraki "
            + "aşamaya geçer ve yolda bir iş akışı çalıştırabilir. İş akışları bu sürecin adımlarıdır: önce burayı okuyun. "
            + "Aşamanın SLA, atama, doküman ve SMS kuralları da aynı satırdadır.", "Aşamalar — "));
        parts.Add(new Paragraph("NormalBullet", "her iş akışı için bir satır. Çalışmanızı buradan seçeceğiniz bir "
            + "satırla başlatın; her sütun ya bir tasarım kararını ya da bir sorunu gösterir.", "Taşıma planı — "));
        parts.Add(new Paragraph("NormalBullet", "hangi akış hangisini çağırıyor. rol sütunu \"yapı taşı\" ise o akış "
            + "tek başına taşınmaz.", "Çağrı ağacı — "));
        parts.Add(new Paragraph("NormalBullet", "bir giriş noktasından başlayan çağrı ağacının tamamı: bir taşıma "
            + "kaleminin gerçek sınırı budur.", "Süreç ağaçları — "));
        parts.Add(new Paragraph("NormalBullet", "ayrıştırıcının okuyamadığı yapılar. Bu akışların diyagramı eksiktir; "
            + "CRM ekranından doğrulayın.", "Okunamayan yapılar — "));
        parts.Add(new Paragraph("NormalBullet", "CRM'deki tanım ile çalışan kopyasının farklı olduğu akışlar. Farklıysa "
            + "üretimde çalışan, çizilen değildir.", "Sapma — "));

        parts.Add(new Paragraph("Heading2", "2.2  bpmn/ — akışın resmi"));
        parts.Add(new Paragraph("Normal", "Plandaki bpmn_dosyasi sütunundaki dosyayı bpmn.io ya da Camunda Modeler "
            + "ile açın. Üstteki not kutusunu okuyun: künye, rol, kullanım ve uyarılar oradadır. Sonra akışı "
            + "soldan sağa izleyin; elmasların üzerindeki metin CRM'deki koşulun kendisidir. Solundaki ad çoğunlukla "
            + "bir alandır (varlik.alan); bir özel etkinliğin döndürdüğü değer karşılaştırılıyorsa o etkinliğin adı "
            + "ve çıktısı yazar (Etkinlik.Cikti)."));
        parts.Add(new Paragraph("Normal", "bpmn/asama-akislari/ klasöründe her talep konusu için bir diyagram vardır: "
            + "birincil aşamadan başlar ve talebin ulaşabileceği her aşamayı gösterir. Olumlu sonuç bir elmastan sağa, "
            + "olumsuz aşağıdan, iptal yukarıdan çıkar; geri dönen bir sonuç diyagramın altından dolaşır. Sonuç üzerindeki "
            + "kutu o sonuçta çalışan iş akışıdır ve içine girildiğinde o akışın kendi adımları görünür. Görüntüleyiciniz "
            + "kutunun içine girmeyi desteklemiyorsa aynı akışın kendi diyagramı bpmn/ altındadır."));

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
        parts.Add(new Paragraph("NormalBullet", "iş akışı OLMAYAN, bir mesaj üzerinde çalışan kod. Taşıma planında hiç "
            + "görünmezler ama yeni üründe karşılıkları kurulmalıdır.", "Eklentiler — "));

    }

    private static void Walkthrough(List<DocumentPart> parts, RunState state, UsageEvidence? usage, WorkflowIr? example)
    {
        parts.Add(new Paragraph("Heading1", "3  Bir iş akışını adım adım çözümleme"));
        if (example is WorkflowIr sample)
        {
            WorkflowIdentity identity = sample.Identity;
            parts.Add(new Paragraph("Normal", "Aşağıdaki sıra, planın ilk satırlarından biri üzerinde anlatılıyor. "
                + "Bu akış bu çalıştırmada gerçekten var; dosyayı açıp birlikte ilerleyebilirsiniz."));
            List<DocumentRow> rows =
            [
                new DocumentRow(["Örnek iş akışı", identity.Name], Header: false),
                new DocumentRow(["Künyesi", $"{identity.Category} · {identity.Mode} · {identity.State} · varlık: {identity.PrimaryEntity ?? "—"}"]),
                new DocumentRow(["Diyagramı", "bpmn/" + state.BpmnFiles.GetValueOrDefault(identity.WorkflowId, "") + ".bpmn"])
            ];
            if (UsageStage.Verdict(identity, usage) is string verdict && verdict.Length > 0)
            {
                rows.Add(new DocumentRow(["Kullanım hükmü", verdict]));
            }
            parts.Add(new Table(rows, [2600, 7000]));
        }

        (string, string)[] steps =
        [
            ("Planda satırı okuyun", "tasima-plani.xlsx → Taşıma planı. kategori, birincil_varlik, tetikleyici ve adim "
                + "sütunları akışın ne olduğunu söyler; bekleme_var evet ise süreç zamana yayılıyor demektir ve tasarımı "
                + "baştan farklıdır. kullanim sütununa bakın ama tek başına karar vermeyin."),
            ("Tek başına mı, parça mı", "Aynı satırda rol sütunu: \"yapı taşı\" ise başka akışlar bunu çağırıyor, tek "
                + "başına taşınmaz. Çağrı ağacı ve Süreç ağaçları sayfalarına bakın."),
            ("Diyagramı açın", "bpmn_dosyasi sütunundaki dosyayı açın, üstteki not kutusunu okuyun, sonra akışı soldan "
                + "sağa izleyin."),
            ("Tetikleyiciyi karara bağlayın", "Akışı ne başlatıyor ve yeni üründe aynı olayın karşılığı var mı? "
                + "Taşıma planındaki tetikleyici ve mod sütunları ile diyagramın başlangıç olayı bunu söyler. "
                + "Bu tasarımın ilk kararıdır; cevabını yazmadan devam etmeyin."),
            ("Beklemeleri işaretleyin", "Diyagramdaki bekleme adımları süreci açık tutar. Her biri için \"ne kadar\" ve "
                + "\"neyi bekliyor\" sorularını cevaplayın."),
            ("Veri bağlarını çıkarın", "yazdigi_varliklar sütunundaki varlıkları veri-analizi.xlsx → Veri ayak izi "
                + "sayfasında aratın. yazan > 1 olan her alan bir sıra kararıdır."),
            ("Dış çağrıları ayırın", "ozel_etkinlikler sütunu doluysa dis-sistemler.xlsx → Dış bağımlılıklar sayfasından "
                + "aynı etkinliği kimlerin çağırdığına bakın. Her biri ayrı bir iş kalemidir."),
            ("Çizime ne kadar güveneceğinizi bilin", "okunamayan_adim sütunu 0 değilse diyagram eksiktir; o adımları CRM "
                + "ekranından doğrulayın. Sapma sayfasında geçiyorsa çalışan kopya farklıdır."),
            ("Süreci çizin", "Tetikleyici, adımlar, kararlar, beklemeler ve dış çağrılar elinizde. Yeni üründeki "
                + "karşılıklarını yazın; karşılığı olmayanları işaretleyin.")
        ];
        for (int index = 0; index < steps.Length; index++)
        {
            parts.Add(new Paragraph("Heading3", WordDocument.Invariant($"3.{index + 1}  {steps[index].Item1}")));
            parts.Add(new Paragraph("Normal", steps[index].Item2));
        }
    }

    private static void Traps(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "4  Nelere dikkat edeceksiniz"));
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
                + "kanıtı değildir. Kod adresi parçalardan birleştiriyorsa hiçbir yerde görünmez."
        })
        {
            parts.Add(new Paragraph("NormalBullet", trap));
        }
    }

    private static void Checklist(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "5  Bitirmeden önce kontrol listesi"));
        foreach (string item in new[]
        {
            "Tetikleyiciyi ve tetikleyen alanları yazdınız.",
            "Bütün dalları izlediniz: her karar noktasının iki tarafı da çizimde var.",
            "Bekleme adımlarının ne kadar beklediğini ve neyi beklediğini not ettiniz.",
            "Çağrılan alt akışları açtınız ve aynı taşıma kalemine bağladınız.",
            "Yazdığı alanları çıkardınız ve aynı alana yazan başka akış olup olmadığına baktınız.",
            "Dış çağrıları ayrı bir iş kalemi olarak yazdınız.",
            "Sürecin kimin yetkisiyle çalıştığını ve elle başlatılıp başlatılmadığını yazdınız.",
            "Okunamayan adım kalmadı; kalanları CRM ekranında doğruladınız.",
            "Her adımın \"yeni üründe karşılığı\" satırı dolu; karşılığı olmayanlar işaretli."
        })
        {
            parts.Add(new Paragraph("NormalBullet", item));
        }
    }

    private static void Glossary(List<DocumentPart> parts)
    {
        parts.Add(new Paragraph("Heading1", "6  Sözlük"));
        parts.Add(new Table(
        [
            new DocumentRow(["Terim", "Anlamı"], Header: true),
            new DocumentRow(["Birincil varlık", "Akışın üzerinde çalıştığı kayıt türü: poliçe, müşteri, talep gibi."]),
            new DocumentRow(["Tetikleyici", "Akışı başlatan olay: kayıt oluşturma, alan güncelleme, silme ya da kullanıcının isteği."]),
            new DocumentRow(["Alt akış", "Başka bir akışın çağırdığı akış. Tek başına değil, çağıranıyla birlikte taşınır."]),
            new DocumentRow(["Özel etkinlik", "CRM'e kaydedilmiş, iş akışının çağırdığı derlenmiş kod. Dışarıya açılan tek kapı budur."]),
            new DocumentRow(["Sapma", "CRM'deki tanım ile o tanımın çalışan kopyasının farklı olması. Farklıysa çalışan kopya geçerlidir."]),
            new DocumentRow(["BPMN", "Süreç çizmenin standardı. .bpmn dosyaları bpmn.io veya Camunda Modeler ile açılır."])
        ], [2600, 7000]));
    }

    /// <summary>
    /// The workflow the walkthrough is written on: a live process with a diagram, and as much of what the guide
    /// talks about as one workflow can carry — an outside call, and more than a handful of steps.
    /// </summary>
    private static WorkflowIr? Example(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        return MigrationPlan.InScope(documents, usage)
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
