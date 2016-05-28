#region Copyright (c) 2016 Atif Aziz. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

namespace WebLinq
{
    #region Imports

    using System;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Net.Http;
    using Html;
    using Mannex.Collections.Specialized;
    using Mannex.Web;

    #endregion

    public static class HttpQuery
    {
        public static HttpSpec Http => new HttpSpec();

        public static Query<HttpResponseMessage> Submit(this Query<HttpResponseMessage> query, string formSelector, NameValueCollection data) =>
            query.Html().Bind(html => Submit(html, formSelector, data));

        public static Query<HttpResponseMessage> Submit(ParsedHtml html, string formSelector, NameValueCollection data) =>
            Query.Create(context => context.Eval((IWebClient wc) =>
            {
                var forms = html.GetForms(formSelector, (fe, id, name, fa, fm, enctype) => fe.GetForm(fd => new
                {
                    Action  = new Uri(html.TryBaseHref(fa), UriKind.Absolute),
                    Method  = fm,
                    EncType = enctype, // TODO validate
                    Data    = fd,
                }));

                var form = forms.FirstOrDefault();
                if (form == null)
                    throw new Exception("No HTML form for submit.");

                if (data != null)
                {
                    foreach (var e in data.AsEnumerable())
                    {
                        form.Data.Remove(e.Key);
                        if (e.Value.Length == 1 && e.Value[0] == null)
                            continue;
                        foreach (var value in e.Value)
                            form.Data.Add(e.Key, value);
                    }
                }

                var submissionResponse =
                    form.Method == HtmlFormMethod.Post
                    ? wc.Post(form.Action, form.Data, null)
                    : wc.Get(new UriBuilder(form.Action) { Query = form.Data.ToW3FormEncoded() }.Uri, null);

                return QueryResult.Create(context, submissionResponse);
            }));
    }
}
