using TOHE;

int checks = 0;
void Expect(string input, string title, string body)
{
    var actual = TemplateManager.ParseTemplate(input);
    if (actual.Title != title || actual.Body != body)
        throw new Exception($"Template split failed at case {checks + 1}");
    checks++;
}

Expect("plain text", "", "plain text");
Expect("", "", "");
Expect("<title>header</title>body", "header", "body");
Expect("before<title>header</title>after", "header", "beforeafter");
Expect("prefix\n<title>header</title>\nbody", "header", "prefix\n\nbody");
Expect("before<title></title>after", "", "beforeafter");
Expect("<title></title>body", "", "body");
Expect("before<title>unfinished", "", "before<title>unfinished");
Expect("before</title>after", "", "before</title>after");
Expect("<title><b><color=#123456>名字</color></b></title>正文", "<b><color=#123456>名字</color></b>", "正文");
Expect("<title><b><color=#ffc0cb【欢迎来到 Town of Host Enhanced】</color></b></title>正文",
    "<b><color=#ffc0cb>【欢迎来到 Town of Host Enhanced】</color></b>", "正文");
Expect("<title><color=#ffc0cb>自定义标题</color></title>body", "<color=#ffc0cb>自定义标题</color>", "body");
Expect("<title>one</title><title>two</title>body", "one", "<title>two</title>body");

var welcome = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "SChinese.txt")).First();
var parsed = TemplateManager.ParseTemplate(welcome[(welcome.IndexOf(':') + 1)..].Replace("\\n", "\n"));
if (parsed.Title != "<b><color=#ffc0cb>【欢迎来到 Town of Host Enhanced】</color></b>" ||
    !parsed.Body.StartsWith("\n<size=100%>") || parsed.Body.Contains("</title>"))
    throw new Exception("Bundled Chinese welcome title/body regression");
checks++;
Console.WriteLine($"PASS: {checks} production template parsing/resource checks.");
