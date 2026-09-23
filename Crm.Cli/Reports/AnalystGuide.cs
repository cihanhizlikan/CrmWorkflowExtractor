using System.Globalization;
using Crm.Cli.Stages;
using Crm.Ir.Model;

namespace Crm.Cli.Reports;

/// <summary>
/// <c>raporlar/nasil-kullanilir.pdf</c>: the one thing an analyst who has never seen this CRM reads first. It says
/// what the package is, which file to open in which order, and how to work through a single workflow from the plan
/// row to a drawn process — demonstrated on a real workflow from THIS run, not on an invented one.
///
/// <para>
/// It is a PDF and not a seventh workbook because it is read once, start to finish, on a screen or on paper, by
/// someone who does not yet know which questions to ask. Everything in it that could go stale — the counts, the
/// example, the file names — is taken from the run that writes it.
/// </para>
/// </summary>
public static class AnalystGuide
{
    public static byte[]? Build(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage, string logoFile)
    {
        TrueTypeFont? font = TrueTypeFont.FindInstalled();
        if (font is null)
        {
            return null;
        }

        PdfDocument pdf = new(font, "CRM İş Akışları — Sistem Analisti Kılavuzu");
        WorkflowIr? example = Example(state, documents, usage);
        Cover(pdf, state, documents, usage, Logo(state, logoFile));
        Background(pdf);
        Files(pdf);
        Walkthrough(pdf, state, usage, example);
        Traps(pdf);
        Checklist(pdf);
        Glossary(pdf);
        return pdf.ToBytes();
    }

    private static void Cover(PdfDocument pdf, RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage, PngImage? logo)
    {
        int inScope = MigrationPlan.InScope(documents, usage).Count();
        pdf.Banner("CRM İş Akışları — Sistem Analisti Kılavuzu",
            "Bu paketle ne yapacaksınız, hangi dosyayı hangi sırayla açacaksınız ve bir iş akışını adım adım nasıl çözümlersiniz.",
            "KURUMSAL MİMARİ · " + Stamp(state.RunId), logo);
        pdf.Lead("Elinizdeki paket, kurumun CRM sisteminde çalışan " + Number(documents.Count)
            + " süreç tanımının okunmuş ve çizilmiş halidir. Bunların " + Number(inScope)
            + " tanesi yeni üründe yeniden kurulacak iştir; kalanı ayrı bir dosyaya alındı. CRM'i hiç görmemiş "
            + "olsanız da bu kılavuzu baştan sona okuyup ilk iş akışınızı aynı gün çözümleyebilirsiniz.");
        pdf.Note("Bu paketteki her şey üretim verisinden üretildi ve kurum dışına çıkarılmamalıdır. Diyagramlar "
            + "açıklayıcıdır: anlamak ve yeniden tasarlamak içindir, çalıştırılamazlar ve CRM'e geri yüklenemezler.");
    }

    private static void Background(PdfDocument pdf)
    {
        pdf.Heading("Beş dakikada arka plan");
        pdf.Body("CRM'de \"iş akışı\", bir kayıt üzerinde bir şey olduğunda sistemin kendiliğinden yaptığı iştir: "
            + "poliçe oluşturulunca bir alan doldurmak, durum değişince e-posta göndermek, bir onay beklemek gibi. "
            + "Kod değil, ekrandan tanımlanmış kurallardır; bu yüzden yıllar içinde çoğalmışlardır.");
        pdf.Spacer(2);
        pdf.Bullet("İş Akışı: arka planda ya da kayıt kaydedilirken çalışan asıl otomasyon. İşin büyük kısmı budur.");
        pdf.Bullet("Diyalog: kullanıcıya soru soran, adım adım ilerleyen ekran akışı.");
        pdf.Bullet("İş Kuralı: form üzerinde çalışan kural (alanı gizle, zorunlu yap). Tarayıcıda çalışır, iz bırakmaz.");
        pdf.Bullet("Eylem: başka akışların çağırdığı, girdi/çıktısı olan yeniden kullanılabilir parça.");
        pdf.Bullet("İş Süreci Akışı: formun üstündeki aşama çubuğu; bu araç onların içini henüz okuyamıyor.");
        pdf.Spacer(4);
        pdf.Body("İki ayrım her yerde karşınıza çıkar. Mod: \"arka plan\" akış kullanıcı beklemeden sonra çalışır, "
            + "\"gerçek zamanlı\" akış kayıt kaydedilirken çalışır ve kullanıcıyı bekletir. Durum: \"taslak\" bir tanım "
            + "yeni çalıştırma başlatamaz, \"etkin\" olan başlatır.");
        pdf.Spacer(4);
        pdf.Body("Araç CRM'e yalnızca OKUMA amacıyla bağlandı; hiçbir şeyi değiştirmedi. Her tanımın kendi XAML "
            + "metnini aldı, adımlarını çözümledi ve her biri için bir BPMN diyagramı üretti. BPMN, süreçleri çizmenin "
            + "uluslararası standardıdır: .bpmn dosyalarını bpmn.io sitesinde veya Camunda Modeler'da açabilirsiniz.");
    }

    /// <summary>
    /// The files in the order they are opened, with every sheet of every workbook and what a reader does on it.
    /// A reader should be able to stop after this section, open the first file and start working.
    /// </summary>
    private static void Files(PdfDocument pdf)
    {
        pdf.Heading("Dosyalar, sekmeler ve açılış sırası");
        pdf.Body("Sıra önemlidir: her adım bir sonrakinde ne arayacağınızı söyler. Her çalışma kitabının ilk sayfası "
            + "\"Nasıl okunur\"dur ve sütunların tek tek ne işe yaradığını yazar; sekmeler de burada anlatılan sırayla dizilidir.");
        pdf.Step(1, "rapor.md — çalıştırmanın özeti",
            "Kaç tanım okundu, kaçı okunamadı, hangi uyarılar var. İki dakika okuyun; bir sayı tuhaf geliyorsa işe başlamadan sorun.");
        pdf.Step(2, "tasima-plani.xlsx — sizin iş listeniz",
            "İşin kendisi. Süzmeniz gerekmez: kurumun kurmadığı, taslak olan ve hiç çalışmamış deneme akışları bu dosyada zaten yoktur.");
        pdf.Bullet("Taşıma planı — her iş akışı için bir satır. Çalışmanızı buradan seçeceğiniz bir satırla başlatın; "
            + "her sütun ya bir tasarım kararını ya da bir sorunu gösterir.", 26);
        pdf.Bullet("Çağrı ağacı — bir akışın kimi çağırdığı ve kimin onu çağırdığı. \"Bu akışı tek başına ele alabilir miyim?\" "
            + "sorusunun cevabı.", 26);
        pdf.Bullet("Süreç ağaçları — bir giriş noktası ve altındaki bütün akışlar, derinlik sırasıyla. Taşıma kalemlerinizi "
            + "bu sayfadan çıkarın: bir ağaç bir kalemdir.", 26);
        pdf.Bullet("Sapma — CRM'de çalışan kopyası tanımından farklı olan akışlar. Buradaki her satır, diyagramın üretimdeki "
            + "davranışı göstermeyebileceği anlamına gelir: o akışı CRM'de açıp doğrulayın.", 26);
        pdf.Bullet("Okunamayan yapılar — aracın çözemediği adımlar. Bir akış burada geçiyorsa diyagramı eksiktir: "
            + "bpmn_dosyasi sütunundaki diyagramı açın, eksik adımlar orada OKUNAMADI olarak işaretlidir.", 26);
        pdf.Step(3, "bpmn/ — akışın resmi",
            "Plan satırındaki bpmn_dosyasi sütunu diyagramın yolunu verir; dosyalar kategori ve varlık klasörlerine ayrılmıştır. "
            + "Her diyagramın üstündeki not kutusu akışın künyesini, rolünü, ailesini, kullanım kaydını ve varsa uyarılarını taşır — "
            + "yani bir diyagramı açtığınızda Excel'e dönmeden temel soruların cevabı oradadır.");
        pdf.Step(4, "aileler.xlsx — hangileri aslında aynı",
            "Bir akışın ailesi varsa onu tek başına tasarlamayın. Tek karar şudur: bunlar yeni üründe tek bir süreç mü olacak?");
        pdf.Bullet("Aileler — üyeler, başlangıç noktasına benzerlikleri ve boş bırakılmış karar sütunu. Kararınızı oraya yazın.", 26);
        pdf.Bullet("Birleştirme — ailenin tek birleşik modeli ve üyeleri. cesitleme_sayisi üyelerin ayrıştığı nokta sayısıdır: "
            + "sıfıra yakınsa birleşme kolay, büyükse tartışmalıdır.", 26);
        pdf.Bullet("Yakın çiftler — aile olmamış ama karara değer çiftler: eşiğe yakın kalanlar ve aynı yapıyı farklı adla "
            + "taşıyanlar, yani kopyalanıp yeniden adlandırılmış olabilecekler.", 26);
        pdf.Step(5, "veri-analizi.xlsx — bir akışı tek başına tasarlayamayacağınız yerler",
            "Tasarladığınız akışın yazdığı alanları burada aratın.");
        pdf.Bullet("Veri ayak izi — alan alan: kaç akış yazıyor, kaç akış okuyor, o alana yazmak neyi başlatıyor. "
            + "yazan > 1 olan alanlarda sıra CRM'de hiçbir zaman garanti edilmedi; yeni üründe bir sıra kararlaştırın.", 26);
        pdf.Bullet("Tetikleme zincirleri — bir akışın yazmasıyla kendiliğinden başlayan başka akışlar. Bu bağ diyagramlarda "
            + "görünmez; yalnızca burada vardır.", 26);
        pdf.Step(6, "dis-sistemler.xlsx — entegrasyon yükü",
            "CRM dışına uzanan çağrılar. Buradaki her etkinlik, yeni üründe ayrı bir iş kalemidir.");
        pdf.Bullet("Dış bağımlılıklar — çağrılan kod, onu çağıran akışlar ve çağrının parametre adları. Parametreler "
            + "arkadaki operasyonu tarif eder: entegrasyon maddelerinizi bu sütundan çıkarın. Kodun içinde ne olduğunu "
            + "yalnızca derlemenin sahibi söyleyebilir.", 26);
        pdf.Bullet("Adresler — tanımın metnine yazılmış adresler, sunucusuyla birlikte. Boş olması \"hiçbir yere bağlanmıyor\" "
            + "demek değildir: adres çoğu zaman etkinliğin kendi kodundadır.", 26);
    }

    private static void Walkthrough(PdfDocument pdf, RunState state, UsageEvidence? usage, WorkflowIr? example)
    {
        pdf.Heading("Bir iş akışını adım adım çözümleme");
        if (example is WorkflowIr sample)
        {
            WorkflowIdentity identity = sample.Identity;
            pdf.Body("Aşağıdaki sıra, planın ilk satırlarından biri üzerinde anlatılıyor. Bu akış bu çalıştırmada "
                + "gerçekten var; dosyayı açıp birlikte ilerleyebilirsiniz.");
            pdf.Spacer(4);
            pdf.Keep(96);
            pdf.Fact("Örnek iş akışı", identity.Name);
            pdf.Fact("Künyesi", $"{identity.Category} · {identity.Mode} · {identity.State} · varlık: {identity.PrimaryEntity ?? "—"}");
            pdf.Fact("Diyagramı", "bpmn/" + state.BpmnFiles.GetValueOrDefault(identity.WorkflowId, "") + ".bpmn");
            // Empty when the run was given no usage file: then the line would say nothing and is left out.
            if (UsageStage.Verdict(identity, usage) is string verdict && verdict.Length > 0)
            {
                pdf.Fact("Kullanım hükmü", verdict);
            }
            pdf.Spacer(6);
        }

        pdf.Step(1, "Planda satırı okuyun",
            "tasima-plani.xlsx → Taşıma planı. kategori, birincil_varlik, tetikleyici ve adim sütunları akışın ne olduğunu "
            + "söyler; bekleme_var evet ise süreç zamana yayılıyor demektir ve tasarımı baştan farklıdır. kullanim sütununa "
            + "bakın ama tek başına karar vermeyin: \"kayıtlı çalışma yok\" kullanılmıyor demek değildir.");
        pdf.Step(2, "Tek başına mı, parça mı",
            "Aynı satırda rol sütunu: \"yapı taşı\" ise başka akışlar bunu çağırıyor, tek başına taşınmaz. "
            + "Çağrı ağacı sayfasından kimin çağırdığına, Süreç ağaçları sayfasından hangi kaleme ait olduğuna bakın.");
        pdf.Step(3, "Diyagramı açın",
            "bpmn_dosyasi sütunundaki dosyayı bpmn.io ya da Camunda Modeler ile açın. Üstteki not kutusunu okuyun: "
            + "künye, rol, aile, kullanım ve uyarılar oradadır. Sonra akışı soldan sağa izleyin; elmasların üzerindeki "
            + "metin CRM'deki koşulun kendisidir.");
        pdf.Step(4, "Tetikleyiciyi karara bağlayın",
            "Akışı ne başlatıyor ve yeni üründe aynı olayın karşılığı var mı? Yoksa olayı kim üretecek? "
            + "Bu, tasarımın ilk kararıdır; cevabı yazmadan devam etmeyin.");
        pdf.Step(5, "Beklemeleri işaretleyin",
            "Diyagramdaki bekleme adımları süreci açık tutar. Her biri için \"ne kadar\" ve \"neyi bekliyor\" sorularını "
            + "cevaplayın: yeni üründe karşılığı bir zamanlayıcı ya da bekleyen bir görevdir.");
        pdf.Step(6, "Ailesine bakın",
            "Plan satırında aile doluysa aileler.xlsx → Aileler sayfasında o aileyi bulun, birlesik_dosya sütunundaki "
            + "birleşik modeli üyelerle karşılaştırın ve kararınızı karar sütununa yazın. Aynı işi birkaç kez tasarlamayın.");
        pdf.Step(7, "Veri bağlarını çıkarın",
            "yazdigi_varliklar sütunundaki varlıkları veri-analizi.xlsx → Veri ayak izi sayfasında aratın. yazan > 1 olan "
            + "her alan bir sıra kararıdır. Tetikleme zincirleri sayfasında akışınız geçiyorsa, kendiliğinden başlattığı "
            + "akışlar vardır ve bunlar diyagramda görünmez.");
        pdf.Step(8, "Dış çağrıları ayırın",
            "ozel_etkinlikler sütunu doluysa dis-sistemler.xlsx → Dış bağımlılıklar sayfasından aynı etkinliği kimlerin "
            + "çağırdığına bakın. Entegrasyonu ayrı bir iş kalemi olarak yazın; içinde ne olduğunu derlemenin sahibine sorun.");
        pdf.Step(9, "Çizime ne kadar güveneceğinizi bilin",
            "okunamayan_adim sıfırdan büyükse diyagramdaki OKUNAMADI kutularını bulun ve karşılıklarını CRM ekranında gözle "
            + "doğrulayın. Akış Sapma sayfasında geçiyorsa üretimde çalışan kopya bu çizimden farklıdır: esas alınacak olan "
            + "çalışan kopyadır.");
        pdf.Step(10, "Süreci çizin",
            "Tek sayfada, soldan sağa: solda tetikleyici, ortada adımlar, kararlar elmas, beklemeler ayrı sembol, dış çağrılar "
            + "ayrı kutu, sağda sonuç. Her kutunun altına \"yeni üründe karşılığı\" satırı açın ve karşılığı olmayanları "
            + "işaretleyin: asıl tartışma o kutular üzerinden yürüyecek.");
        pdf.Note("Çizerken CRM'deki adımları birebir kopyalamayın. Amaç, işin ne olduğunu göstermektir: aynı sonucu "
            + "veren daha kısa bir akış, yeni üründe doğru tasarımdır.");
    }

    private static void Traps(PdfDocument pdf)
    {
        pdf.Heading("Nelere dikkat edeceksiniz");
        pdf.Bullet("Planın kullanim sütunundaki \"kayıtlı çalışma yok\", kullanılmadığını KANITLAMAZ. CRM sistem işlerini "
            + "düzenli olarak siler, iş kuralları tarayıcıda çalışıp hiç iz bırakmaz, gerçek zamanlı akışlar yalnızca hatayı "
            + "kaydeder. Bu sütun bir silme gerekçesi değildir.");
        pdf.Bullet("Ada bakarak temizlik yapmayın. Adında DRAFT, TEST ya da ESKİ geçen ve üretimde her gün çalışan "
            + "akışlar bulundu; bu yüzden ad tek başına kapsam dışı bırakma gerekçesi sayılmadı.");
        pdf.Bullet("İş kuralları tarayıcıda çalışır ve hiçbir kayıt bırakmaz: onların kullanımı hakkında hiçbir "
            + "kanıt yoktur, olmaması da bir şey anlatmaz.");
        pdf.Bullet("Gerçek zamanlı akışlar kullanıcıyı bekletir. Yeni üründe aynı işi arka plana almak davranışı "
            + "değiştirir; bunu bilerek karar verin.");
        pdf.Bullet("Tetikleme zincirleri diyagramda görünmez. Bir akış, başka bir akışın izlediği alana yazdığı için "
            + "onu başlatıyor olabilir; bu bağ yalnızca veri-analizi.xlsx'tedir.");
        pdf.Bullet("Özel etkinliklerin içi görünmez. Adresler sayfası yalnızca tanımın metnine yazılmış adresleri "
            + "gösterir; çağrılan servisin adresi çoğu zaman etkinliğin kendi kodunda ya da konfigürasyonundadır ve "
            + "oradan görünmez — onu derlemenin sahibi söyler. Eklentiler (plug-in) ise iş akışı değildir, bu "
            + "envantere hiç girmezler.");
        pdf.Bullet("Birleşik model bir öneridir, karar değil. Araç yalnızca benzerliği ölçer; aynı işi yapıp "
            + "yapmadıklarına insan karar verir.");
    }

    private static void Checklist(PdfDocument pdf)
    {
        pdf.Heading("Bir iş akışını bitirmeden emin olun");
        pdf.Bullet("Tetikleyiciyi ve tetikleyen alanları yazdınız.");
        pdf.Bullet("Bütün dalları izlediniz: her karar noktasının iki tarafı da çizimde var.");
        pdf.Bullet("Bekleme adımlarının ne kadar beklediğini ve neyi beklediğini not ettiniz.");
        pdf.Bullet("Çağrılan alt akışları açtınız ve aynı taşıma kalemine bağladınız.");
        pdf.Bullet("Yazdığı alanları çıkardınız ve aynı alana yazan başka akış olup olmadığına baktınız.");
        pdf.Bullet("Dış çağrıları ayrı bir iş kalemi olarak yazdınız.");
        pdf.Bullet("Ailesindeki diğer akışlara baktınız ve birleştirme kararınızı yazdınız.");
        pdf.Bullet("Okunamayan adım kalmadı; kalanları CRM ekranında doğruladınız.");
        pdf.Bullet("Her adımın \"yeni üründe karşılığı\" satırı dolu; karşılığı olmayanlar işaretli.");
    }

    private static void Glossary(PdfDocument pdf)
    {
        pdf.Heading("Sözlük");
        pdf.Fact("Birincil varlık", "Akışın üzerinde çalıştığı kayıt türü: poliçe, müşteri, talep gibi.");
        pdf.Fact("Tetikleyici", "Akışı başlatan olay: kayıt oluşturma, alan güncelleme, silme ya da kullanıcının isteği.");
        pdf.Fact("Alt akış", "Başka bir akışın çağırdığı akış. Tek başına değil, çağıranıyla birlikte taşınır.");
        pdf.Fact("Özel etkinlik", "CRM'e kaydedilmiş, iş akışının çağırdığı derlenmiş kod. Dışarıya açılan tek kapı budur.");
        pdf.Fact("Aile", "Birbirine çok benzeyen akışlar kümesi. Adı, kümenin başlangıç noktası olan akıştır.");
        pdf.Fact("Birleşik model", "Bir ailenin bütün üyelerinin kapsandığı tek model. Öneridir, karar sizindir.");
        pdf.Fact("Çeşitleme", "Birleşik modelde üyelerin ayrıştığı nokta: burada üyeler farklı işler yapıyor.");
        pdf.Fact("Sapma", "CRM'deki tanım ile o tanımın çalışan kopyasının farklı olması. Farklıysa çalışan kopya geçerlidir.");
        pdf.Fact("Ara model", "Aracın XAML'den çıkardığı, diyagramların ve tabloların üretildiği ortak biçim.");
        pdf.Fact("BPMN", "Süreç çizmenin standardı. .bpmn dosyaları bpmn.io veya Camunda Modeler ile açılır.");
    }

    /// <summary>
    /// The workflow the walkthrough is written on: a live process with a diagram, and as much of what the guide
    /// talks about as one workflow can carry — an outside call, a family, more than a handful of steps.
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
    private static PngImage? Logo(RunState state, string logoFile)
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
            return PngImage.TryRead(copy.ToArray());
        }

        string path = Path.IsPathRooted(logoFile) ? logoFile : Path.Combine(AppContext.BaseDirectory, logoFile);
        PngImage? picture = File.Exists(path) ? PngImage.TryRead(File.ReadAllBytes(path)) : null;
        if (picture is null)
        {
            state.Warnings.Add($"Run:LogoFile olarak verilen '{path}' okunamadı (8 bitlik, katmansız bir PNG bekleniyor); kılavuz logosuz üretildi.");
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
