using System.IO.Compression;
using System.Text;

// Builds fictional Canvas course exports (IMS Common Cartridge layout) in memory for tests and the demo sample.
public sealed class ExportFixture
{
    public string CourseId = "g0fictional0course0export0001";
    public string Title = "Teaching Statistics with Real Data";
    public string Code = "STAT 110";
    public string WelcomeBody = "<h2>Welcome</h2><p>This fictional course uses open datasets.</p><img src=\"$IMS-CC-FILEBASE$/banner.png\" alt=\"banner.png\">";
    // A wiki page that happens to be called "Syllabus". Distinct from CourseSyllabusBody below, which is the Canvas
    // course syllabus: a property of the course, exported to course_settings/syllabus.html.
    public bool IncludeSyllabusPage = true;
    public string? CourseSyllabusBody;
    // Adds a complete classic quiz (a survey with two questions) and a module text header.
    public bool IncludeImportableQuiz;
    // Adds a graded discussion, an announcement, an external-tool assignment and a New Quiz.
    public bool IncludeActivities;
    public readonly Dictionary<string, byte[]> Extra = new();

    public static string Manifest(string courseId, string resources) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest identifier="{courseId}" xmlns="http://www.imsglobal.org/xsd/imsccv1p1/imscp_v1p1">
          <metadata><lom xmlns="http://ltsc.ieee.org/xsd/imsccv1p1/LOM/resource"><general><title><string>Fictional export</string></title></general></lom></metadata>
          <organizations/>
          <resources>{resources}</resources>
        </manifest>
        """;

    public byte[] Build()
    {
        var files = new Dictionary<string, string>
        {
            ["imsmanifest.xml"] = Manifest(CourseId, $"""
                <resource identifier="page-welcome" type="webcontent" href="wiki_content/welcome.html"><file href="wiki_content/welcome.html"/></resource>
                {(IncludeSyllabusPage ? "<resource identifier=\"page-syllabus\" type=\"webcontent\" href=\"wiki_content/syllabus.html\"><file href=\"wiki_content/syllabus.html\"/></resource>" : "")}
                <resource identifier="asg-project" type="associatedcontent/imscc_xmlv1p1/learning-application-resource" href="asg-project/data-project.html">
                  <file href="asg-project/data-project.html"/><file href="asg-project/assignment_settings.xml"/></resource>
                <resource identifier="quiz-1" type="imsqti_xmlv1p2/imscc_xmlv1p1/assessment"><file href="quiz-1/assessment_qti.xml"/></resource>
                <resource identifier="disc-1" type="imsdt_xmlv1p1"><file href="disc-1.xml"/></resource>
                {(IncludeImportableQuiz ? "<resource identifier=\"quiz-2\" type=\"imsqti_xmlv1p2/imscc_xmlv1p1/assessment\"><file href=\"quiz-2/assessment_qti.xml\"/></resource>" : "")}
                {(IncludeActivities ? ActivityResources : "")}
                <resource identifier="file-1" type="webcontent" href="web_resources/banner.png"><file href="web_resources/banner.png"/></resource>
                """),
            ["course_settings/course_settings.xml"] = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <course identifier="{CourseId}" xmlns="http://canvas.instructure.com/xsd/cccv1p0"><title>{Title}</title><course_code>{Code}</course_code></course>
                """,
            ["course_settings/module_meta.xml"] = $$"""
                <?xml version="1.0" encoding="UTF-8"?>
                <modules xmlns="http://canvas.instructure.com/xsd/cccv1p0">
                  <module identifier="mod-start"><title>Start here</title><workflow_state>active</workflow_state><position>1</position><items>
                    <item identifier="item-welcome"><content_type>WikiPage</content_type><workflow_state>active</workflow_state><title>Welcome</title><identifierref>page-welcome</identifierref><position>1</position></item>
                    <item identifier="item-quiz"><content_type>Quizzes::Quiz</content_type><workflow_state>active</workflow_state><title>Check your understanding</title><identifierref>quiz-1</identifierref><position>2</position></item>
                  </items></module>
                  <module identifier="mod-week1"><title>Week 1: Describing data</title><workflow_state>unpublished</workflow_state><position>2</position><items>
                    <item identifier="item-project"><content_type>Assignment</content_type><workflow_state>active</workflow_state><title>Data project</title><identifierref>asg-project</identifierref><position>1</position></item>
                    {{QuizItems}}{{ActivityItems}}
                  </items></module>
                </modules>
                """,
            ["wiki_content/welcome.html"] = $"<html><head><title>Welcome</title><meta name=\"workflow_state\" content=\"active\"/></head><body>{WelcomeBody}</body></html>",
            ["asg-project/data-project.html"] = "<html><head><title>Data project</title></head><body><p>Analyze a public dataset and write a short report.</p><table><tr><td>Part</td><td>Points</td></tr></table></body></html>",
            ["asg-project/assignment_settings.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <assignment identifier="asg-project" xmlns="http://canvas.instructure.com/xsd/cccv1p0"><title>Data project</title><due_at></due_at>
                  <points_possible>25.0</points_possible><workflow_state>published</workflow_state><rubric_identifierref>rubric-1</rubric_identifierref></assignment>
                """,
        };
        if (IncludeImportableQuiz)
        {
            files["quiz-2/assessment_meta.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <quiz identifier="quiz-2" xmlns="http://canvas.instructure.com/xsd/cccv1p0"><title>Week 1 check-in</title>
                  <description>&lt;p&gt;Tell us how the first week went.&lt;/p&gt;</description>
                  <quiz_type>survey</quiz_type><points_possible>0.0</points_possible><allowed_attempts>1</allowed_attempts><available>false</available>
                  <assignment identifier="quiz-2-assignment"><due_at>2026-11-02T06:59:00</due_at><workflow_state>unpublished</workflow_state></assignment></quiz>
                """;
            files["non_cc_assessments/quiz-2.xml.qti"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <questestinterop xmlns="http://www.imsglobal.org/xsd/ims_qtiasiv1p2"><assessment ident="quiz-2" title="Week 1 check-in"><section ident="root_section">
                  <item ident="q1" title="Pace"><itemmetadata><qtimetadata><qtimetadatafield><fieldlabel>question_type</fieldlabel><fieldentry>essay_question</fieldentry></qtimetadatafield></qtimetadata></itemmetadata>
                    <presentation><material><mattext texttype="text/html">&lt;p&gt;Explain how the pace of the first week felt.&lt;/p&gt;</mattext></material></presentation></item>
                  <item ident="q2" title="Chart"><itemmetadata><qtimetadata><qtimetadatafield><fieldlabel>question_type</fieldlabel><fieldentry>multiple_choice_question</fieldentry></qtimetadatafield></qtimetadata></itemmetadata>
                    <presentation><material><mattext texttype="text/html">&lt;p&gt;Which chart was clearest?&lt;/p&gt;&lt;img src="chart.png"&gt;</mattext></material></presentation></item>
                </section></assessment></questestinterop>
                """;
        }
        if (IncludeActivities)
        {
            files["disc-2.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <topic xmlns="http://www.imsglobal.org/xsd/imsccv1p1/imsdt_v1p1"><title>Introduce yourself</title>
                  <text texttype="text/html">&lt;p&gt;Say hello and share one goal.&lt;/p&gt;</text></topic>
                """;
            files["disc-2-meta.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <topicMeta identifier="disc-2-meta" xmlns="http://canvas.instructure.com/xsd/cccv1p0"><topic_id>disc-2</topic_id><title>Introduce yourself</title>
                  <type>topic</type><workflow_state>active</workflow_state>
                  <assignment identifier="disc-2-assignment"><points_possible>5.0</points_possible><due_at>2026-11-03T06:59:00</due_at></assignment></topicMeta>
                """;
            files["ann-1.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <topic xmlns="http://www.imsglobal.org/xsd/imsccv1p1/imsdt_v1p1"><title>Welcome!</title><text texttype="text/html">&lt;p&gt;Hi all.&lt;/p&gt;</text></topic>
                """;
            files["ann-1-meta.xml"] = """
                <?xml version="1.0" encoding="UTF-8"?>
                <topicMeta identifier="ann-1-meta" xmlns="http://canvas.instructure.com/xsd/cccv1p0"><topic_id>ann-1</topic_id><type>announcement</type><workflow_state>active</workflow_state></topicMeta>
                """;
            foreach (var (id, title, url) in new[] { ("asg-tool", "Lab in CodeGrade", "https://app.codegra.de/lti/launch"), ("asg-nq", "Unit 1 New Quiz", "https://iad.quiz-lti-iad-prod.instructure.com/lti/launch") })
            {
                files[$"{id}/{id}.html"] = $"<html><head><title>{title}</title></head><body><p>Complete it in the tool.</p></body></html>";
                files[$"{id}/assignment_settings.xml"] = $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <assignment identifier="{id}" xmlns="http://canvas.instructure.com/xsd/cccv1p0"><title>{title}</title><points_possible>20.0</points_possible>
                      <workflow_state>published</workflow_state><submission_types>external_tool</submission_types><external_tool_url>{url}</external_tool_url></assignment>
                    """;
            }
        }
        // Course files are listed, never opened.
        files["web_resources/banner.png"] = "not really a png";
        if (IncludeSyllabusPage) files["wiki_content/syllabus.html"] = "<html><head><title>Syllabus</title><meta name=\"workflow_state\" content=\"unpublished\"/></head><body><p>Draft syllabus.</p></body></html>";
        if (CourseSyllabusBody is not null) files["course_settings/syllabus.html"] = CourseSyllabusBody;
        return Zip(files.Select(f => (f.Key, Encoding.UTF8.GetBytes(f.Value))).Concat(Extra.Select(e => (e.Key, e.Value))));
    }

    // Writes this fixture plus one large entry of zeros straight to disk, streaming it in chunks, so tests can model exports of
    // hundreds of megabytes without holding them in memory.
    public static void WriteWithLargeEntry(string path, ExportFixture fixture, string name, long size, CompressionLevel level)
    {
        using var output = File.Create(path);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);
        using (var source = new ZipArchive(new MemoryStream(fixture.Build()), ZipArchiveMode.Read))
            foreach (var entry in source.Entries)
            {
                using var from = entry.Open();
                using var to = zip.CreateEntry(entry.FullName, CompressionLevel.Fastest).Open();
                from.CopyTo(to);
            }
        using var large = zip.CreateEntry(name, level).Open();
        var chunk = new byte[1024 * 1024];
        for (long written = 0; written < size; written += chunk.Length) large.Write(chunk, 0, (int)Math.Min(chunk.Length, size - written));
    }

    private const string ActivityResources = """
        <resource identifier="disc-2" type="imsdt_xmlv1p1"><file href="disc-2.xml"/><dependency identifierref="disc-2-meta"/></resource>
        <resource identifier="disc-2-meta" type="associatedcontent/imscc_xmlv1p1/learning-application-resource" href="disc-2-meta.xml"><file href="disc-2-meta.xml"/></resource>
        <resource identifier="ann-1" type="imsdt_xmlv1p1"><file href="ann-1.xml"/><dependency identifierref="ann-1-meta"/></resource>
        <resource identifier="ann-1-meta" type="associatedcontent/imscc_xmlv1p1/learning-application-resource" href="ann-1-meta.xml"><file href="ann-1-meta.xml"/></resource>
        <resource identifier="asg-tool" type="associatedcontent/imscc_xmlv1p1/learning-application-resource" href="asg-tool/asg-tool.html"><file href="asg-tool/asg-tool.html"/><file href="asg-tool/assignment_settings.xml"/></resource>
        <resource identifier="asg-nq" type="associatedcontent/imscc_xmlv1p1/learning-application-resource" href="asg-nq/asg-nq.html"><file href="asg-nq/asg-nq.html"/><file href="asg-nq/assignment_settings.xml"/></resource>
        """;
    private string ActivityItems => !IncludeActivities ? "" :
        "<item identifier=\"item-disc2\"><content_type>DiscussionTopic</content_type><workflow_state>active</workflow_state><title>Introduce yourself</title><identifierref>disc-2</identifierref><position>4</position></item>" +
        "<item identifier=\"item-nq\"><content_type>Assignment</content_type><workflow_state>active</workflow_state><title>Unit 1 New Quiz</title><identifierref>asg-nq</identifierref><position>5</position></item>";

    private string QuizItems => !IncludeImportableQuiz ? "" :
        "<item identifier=\"item-header\"><content_type>ContextModuleSubHeader</content_type><workflow_state>active</workflow_state><title>Check in</title><position>2</position></item>" +
        "<item identifier=\"item-quiz2\"><content_type>Quizzes::Quiz</content_type><workflow_state>unpublished</workflow_state><title>Week 1 check-in</title><identifierref>quiz-2</identifierref><position>3</position></item>";

    public static byte[] Zip(IEnumerable<(string Name, byte[] Data)> entries)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                stream.Write(data);
            }
        return buffer.ToArray();
    }
}
