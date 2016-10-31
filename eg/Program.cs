namespace WebLinq.Samples
{
    #region Imports

    using System;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Xml.Linq;
    using Text;
    using Xml;
    using static HttpQuery;
    using static Sys.SysQuery;
    using static Html.HtmlQuery;

    #endregion

    static class Program
    {
        public static void Main()
        {
            Sample1();
            Sample2();
        }

        static void Sample1()
        {
            var q =
                from com in Http.UserAgent(@"Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko")
                    .Get(new Uri("http://www.example.com/"))
                    .Html()
                select new {com.Id, Html = com.Content.QuerySelector("p")?.OuterHtml}
                into com
                from net in Http.Get(new Uri("http://www.example.net/"))
                    .Html()
                from link in Links(net.Content)
                select new
                {
                    Com = com,
                    Net = new
                    {
                        net.Id,
                        Html = net.Content.QuerySelector("p")?.OuterHtml,
                        Link = link,
                    }
                }
                into e
                where e.Com.Html?.Length == e.Net.Html?.Length
                select e;

            q.Dump();
        }

        static void Sample2()
        {
            var ns = XNamespace.Get("http://schemas.microsoft.com/windows/2004/02/mit/task");

            var q =
                from xml in Spawn("schtasks", @"/query /xml ONE").Delimited(Environment.NewLine)
                from doc in XmlQuery.Xml(new StringContent(xml))
                let execs =
                    from t in doc.Elements("Tasks").Elements(ns + "Task")
                    from e in t.Elements(ns + "Actions").Elements(ns + "Exec")
                    select new
                    {
                        Name      = ((XComment)t.PreviousNode).Value.Trim(),
                        Command   = (string)e.Element(ns + "Command"),
                        Arguments = (string)e.Element(ns + "Arguments"),
                    }
                from e in execs.ToQuery()
                select e;

            q.Dump();
        }

        static void Dump<T>(this Query<T> query, TextWriter output = null)
        {
            output = output ?? Console.Out;
            foreach (var e in query.ToEnumerable(DefaultQueryContext.Create))
                output.WriteLine(e);
        }
    }
}
