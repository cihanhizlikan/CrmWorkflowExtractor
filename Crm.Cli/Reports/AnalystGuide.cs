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
    public static byte[]? Build(RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        TrueTypeFont? font = TrueTypeFont.FindInstalled();
        if (font is null)
        {
            return null;
        }

        PdfDocument pdf = new(font, "CRM İş Akışları — Sistem Analisti Kılavuzu");
        WorkflowIr? example = Example(state, documents, usage);
        Cover(pdf, state, documents, usage);
        Background(pdf);
        Order(pdf, state);
        Walkthrough(pdf, state, usage, example);
        Traps(pdf);
        Checklist(pdf);
        Glossary(pdf);
        return pdf.ToBytes();
    }

    private static void Cover(PdfDocument pdf, RunState state, IReadOnlyList<WorkflowIr> documents, UsageEvidence? usage)
    {
        int inScope = MigrationPlan.InScope(documents, usage).Count();
        pdf.Banner("CRM İş Akışları — Sistem Analisti Kılavuzu",
            "Bu paketle ne yapacaksınız, hangi dosyayı hangi sırayla açacaksınız ve bir iş akışını adım adım nasıl çözümlersiniz.",
            "KURUMSAL MİMARİ · " + Stamp(state.RunId));
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

    private static void Order(PdfDocument pdf, RunState state)
    {
        pdf.Heading("Dosyaları hangi sırayla açacaksınız");
        pdf.Body("Sıra önemlidir: her adım bir sonrakinde ne arayacağınızı söyler. İlk üç adım yarım saatinizi alır.");
        pdf.Step(1, "rapor.md — çalıştırmanın özeti",
            "Kaç tanım okundu, kaçı okunamadı, hangi uyarılar var. Sayıların birbirini tuttuğunu burada görürsünüz. "
            + "İki dakika okuyun; bir sayı tuhaf geliyorsa işe başlamadan sorun.");
        pdf.Step(2, "tasima-plani.xlsx — sizin iş listeniz",
            "Önce \"Nasıl okunur\" sayfası, sonra \"Taşıma planı\". Her satır bir iş akışıdır ve taşınacak işi "
            + "anlatır. Süzmeniz gerekmez: kurumun kurmadığı, taslak olan ve hiç çalışmamış deneme akışları bu "
            + "dosyada zaten yoktur. Çalışmanızı bu sayfadan seçeceğiniz bir satırla başlatın.");
        pdf.Step(3, "bpmn/ klasörü — akışın resmi",
            "Plan satırındaki bpmn_dosyasi sütunu, o akışın diyagramının yolunu verir. Dosyalar kategori ve birincil "
            + "varlık klasörlerine ayrılmıştır. Diyagramın üstündeki not kutusu akışın künyesini ve neyle "
            + "başladığını söyler.");
        pdf.Step(4, "aileler.xlsx — hangileri aslında aynı",
            "Birbirine çok benzeyen akışlar \"aile\" olarak gruplandı ve her ailenin birleşik bir modeli çizildi. "
            + "Aynı işi yapıp yapmadıklarına siz karar verirsiniz; kararınızı \"karar\" sütununa yazın.");
        pdf.Step(5, "veri-analizi.xlsx — ne neye dokunuyor",
            "Aynı alana birden fazla akış yazıyorsa sıra garantisi yoktur; bunu yeni üründe siz kararlaştıracaksınız. "
            + "\"Tetikleme zincirleri\" sayfası, bir akışın yazmasıyla başlayan başka akışları gösterir — bu bağ "
            + "diyagramlarda görünmez.");
        pdf.Step(6, "dis-sistemler.xlsx — entegrasyon yükü",
            "CRM dışına uzanan her çağrı buradadır. Bir iş akışı kendi başına servis çağıramaz; yalnızca CRM'e "
            + "kaydedilmiş özel bir etkinlik (derlenmiş kod) üzerinden çağırır. Yeni üründe bu kodun karşılığını "
            + "ayrıca planlamanız gerekir.");
        pdf.Step(7, "kapsam-disi.xlsx — yalnızca gerekirse",
            "Plandan çıkarılan akışlar ve çıkarma gerekçeleri. Buradaki hiçbir satır için iş planlamayın; dosyayı "
            + "sadece bir çıkarma kararını sorgulamak istediğinizde açın.");
        pdf.Note("hassas-degerler.md dosyası kısıtlıdır: XAML içinde bulunan adres, kullanıcı adı ve parola benzeri "
            + "değerleri taşır. Bilgi güvenliği ekibi içindir, analiz paketine konmaz.");
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

        pdf.Step(1, "Satırın künyesini okuyun",
            "kategori, birincil_varlik, tetikleyici, adim ve rol sütunları akışın ne olduğunu bir bakışta söyler. "
            + "rol = \"yapı taşı\" ise bu akış başka akışlar tarafından çağrılıyor demektir: onu tek başına taşımayın.");
        pdf.Step(2, "Diyagramı açın ve not kutusunu okuyun",
            "Her diyagramın üstünde bir not kutusu vardır: akışın adı, künyesi ve \"Şununla başlar\" satırı. "
            + "Diyagramı soldan sağa okuyun; elmas şeklindeki karar noktalarının üzerindeki metin, CRM'deki koşulun "
            + "kendisidir.");
        pdf.Step(3, "Tetikleyiciyi doğrulayın",
            "Akışı ne başlatıyor: kayıt oluşturma, belirli alanların güncellenmesi, silme, yoksa kullanıcının isteği "
            + "mi? Yeni üründe aynı olayın karşılığı var mı, yoksa olayı siz mi üreteceksiniz? Cevabı yazın.");
        pdf.Step(4, "Bekleme adımlarını işaretleyin",
            "\"Bekle\" adımı varsa süreç saatlerce ya da günlerce açık kalıyor demektir. Yeni üründe bunun karşılığı "
            + "bir zamanlayıcı ya da bir bekleyen görevdir; tasarımı buna göre kurun.");
        pdf.Step(5, "Alt akışları açın",
            "Akış başka bir akışı çağırıyorsa (plan satırında rol ve Çağrı ağacı sayfası), onların diyagramlarını da "
            + "açın. Bir üst akış ve çağırdığı alt akışlar TEK bir taşıma kalemidir; ayrı ayrı saymayın.");
        pdf.Step(6, "Yazdığı veriyi çıkarın",
            "yazdigi_varliklar ve yazdigi_alanlar sütunları akışın neye dokunduğunu söyler. Aynı alana yazan başka "
            + "akış var mı diye veri-analizi.xlsx'e bakın: varsa hangi sıranın doğru olduğuna iş birimiyle karar verin.");
        pdf.Step(7, "Dış çağrıları ayırın",
            "ozel_etkinlikler sütunu doluysa bu akış CRM dışına uzanıyor. Etkinliğin içinde ne olduğu tanımda "
            + "görünmez; dis-sistemler.xlsx'ten aynı etkinliği kimlerin çağırdığına bakın ve entegrasyonu ayrı bir "
            + "iş kalemi olarak yazın.");
        pdf.Step(8, "Ailesine bakın",
            "aile sütunu doluysa benzer akışlar var demektir. birlesik/ klasöründeki birleşik modeli açın ve "
            + "üyelerle karşılaştırın. Gerçekten aynı işi yapıyorlarsa yeni üründe tek bir süreç kurarsınız — bu "
            + "kararı aileler.xlsx'teki \"karar\" sütununa yazın.");
        pdf.Step(9, "Okunamayan adımları elle doğrulayın",
            "okunamayan_adim sütunu sıfırdan büyükse aracın çözemediği adımlar var demektir. O akışı CRM ekranında "
            + "açıp ilgili adımı gözle kontrol edin; hangi yapının okunamadığı \"Okunamayan yapılar\" sayfasındadır.");
        pdf.Step(10, "Süreci çizin",
            "Tek sayfada, soldan sağa: solda tetikleyici, ortada adımlar, kararlar elmas, dış çağrılar ayrı bir kutu, "
            + "bekleme adımı ayrı bir sembol, sağda sonuç. Her kutunun altına \"yeni üründe karşılığı\" satırı açın ve "
            + "karşılığı olmayanları kırmızıyla işaretleyin: asıl tartışma o kutular üzerinden yürüyecek.");
        pdf.Note("Çizerken CRM'deki adımları birebir kopyalamayın. Amaç, işin ne olduğunu göstermektir: aynı sonucu "
            + "veren daha kısa bir akış, yeni üründe doğru tasarımdır.");
    }

    private static void Traps(PdfDocument pdf)
    {
        pdf.Heading("Nelere dikkat edeceksiniz");
        pdf.Bullet("Kayıt bulunmaması kullanılmadığını KANITLAMAZ. CRM sistem işlerini düzenli olarak siler; "
            + "\"kayıtlı çalışma yok\" yalnızca elimizdeki kanıtta iz olmadığını söyler.");
        pdf.Bullet("Ada bakarak temizlik yapmayın. Adında DRAFT, TEST ya da ESKİ geçen ve üretimde her gün çalışan "
            + "akışlar bulundu; bu yüzden ad tek başına kapsam dışı bırakma gerekçesi sayılmadı.");
        pdf.Bullet("İş kuralları tarayıcıda çalışır ve hiçbir kayıt bırakmaz: onların kullanımı hakkında hiçbir "
            + "kanıt yoktur, olmaması da bir şey anlatmaz.");
        pdf.Bullet("Gerçek zamanlı akışlar kullanıcıyı bekletir. Yeni üründe aynı işi arka plana almak davranışı "
            + "değiştirir; bunu bilerek karar verin.");
        pdf.Bullet("Tetikleme zincirleri diyagramda görünmez. Bir akış, başka bir akışın izlediği alana yazdığı için "
            + "onu başlatıyor olabilir; bu bağ yalnızca veri-analizi.xlsx'tedir.");
        pdf.Bullet("Özel etkinliklerin içi görünmez. Kodun içine gömülü bir adres ya da kural bu pakette yoktur; "
            + "eklentiler (plug-in) ise iş akışı değildir, bu envantere hiç girmezler.");
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
