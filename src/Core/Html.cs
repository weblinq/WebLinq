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
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Mime;
    using System.Runtime.CompilerServices;
    using Fizzler.Systems.HtmlAgilityPack;
    using HtmlAgilityPack;
    using TryParsers;

    public interface IHtmlParser
    {
        ParsedHtml Parse(string html, Uri baseUrl);
    }

    public abstract class ParsedHtml
    {
        readonly Uri _baseUrl;
        readonly Lazy<Uri> _inlineBaseUrl;

        protected ParsedHtml() :
            this(null) {}

        protected ParsedHtml(Uri baseUrl)
        {
            _baseUrl = baseUrl;
            _inlineBaseUrl = new Lazy<Uri>(TryGetInlineBaseUrl);
        }

        public Uri BaseUrl => _baseUrl ?? _inlineBaseUrl.Value;

        Uri TryGetInlineBaseUrl()
        {
            var baseRef = QuerySelector("html > head > base[href]")?.GetAttributeValue("href");

            if (baseRef == null)
                return null;

            var baseUrl = TryParse.Uri(baseRef, UriKind.Absolute);

            return baseUrl.Scheme == Uri.UriSchemeHttp || baseUrl.Scheme == Uri.UriSchemeHttps
                 ? baseUrl : null;
        }

        public IEnumerable<HtmlObject> QuerySelectorAll(string selector) =>
            QuerySelectorAll(selector, null);

        public abstract IEnumerable<HtmlObject> QuerySelectorAll(string selector, HtmlObject context);

        public HtmlObject QuerySelector(string selector) =>
            QuerySelector(selector, null);

        public virtual HtmlObject QuerySelector(string selector, HtmlObject context) =>
            QuerySelectorAll(selector, context).FirstOrDefault();

        public abstract HtmlObject Root { get; }

        public override string ToString() => Root?.OuterHtml ?? string.Empty;
    }

    public abstract class HtmlObject
    {
        public abstract ParsedHtml Owner { get; }
        public abstract string Name { get; }
        public virtual bool HasAttributes => AttributeNames.Any();
        public abstract IEnumerable<string> AttributeNames { get; }
        public abstract bool HasAttribute(string name);
        public abstract string GetAttributeValue(string name);
        public abstract string OuterHtml { get; }
        public abstract string InnerHtml { get; }
        public abstract string InnerText { get; }
        public virtual bool HasChildElements => ChildElements.Any();
        public abstract IEnumerable<HtmlObject> ChildElements { get; }
        public override string ToString() => OuterHtml;
    }

    public enum HtmlControlType { Input, Select, TextArea }
    public enum HtmlDisabledFlag { Default, Disabled }
    public enum HtmlReadOnlyFlag { Default, ReadOnly }
    public enum HtmlFormMethod { Get, Post }

    public sealed class HapHtmlParser : IHtmlParser
    {
        readonly QueryContext _context;

        public HapHtmlParser(QueryContext context)
        {
            _context = context;
        }

        public ParsedHtml Parse(string html, Uri baseUrl)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml2(html);
            return new HapParsedHtml(doc, baseUrl);
        }

        sealed class HapParsedHtml : ParsedHtml
        {
            readonly HtmlDocument _document;
            readonly ConditionalWeakTable<HtmlNode, HtmlObject> _map = new ConditionalWeakTable<HtmlNode, HtmlObject>();

            public HapParsedHtml(HtmlDocument document, Uri baseUrl) :
                base(baseUrl)
            {
                _document = document;
            }

            HtmlObject GetPublicObject(HtmlNode node)
            {
                HtmlObject obj;
                if (!_map.TryGetValue(node, out obj))
                    _map.Add(node, obj = new HapHtmlObject(node, this));
                return obj;
            }

            public override IEnumerable<HtmlObject> QuerySelectorAll(string selector, HtmlObject context) =>
                (_document.DocumentNode ?? ((HapHtmlObject)context).Node).QuerySelectorAll(selector).Select(GetPublicObject);

            public override HtmlObject Root => GetPublicObject(_document.DocumentNode);

            sealed class HapHtmlObject : HtmlObject
            {
                readonly HapParsedHtml _owner;

                public HapHtmlObject(HtmlNode node, HapParsedHtml owner)
                {
                    Node = node;
                    _owner = owner;
                }

                public override ParsedHtml Owner => _owner;
                public HtmlNode Node { get; }
                public override string Name => Node.Name;

                public override IEnumerable<string> AttributeNames =>
                    from a in Node.Attributes select a.Name;

                public override bool HasAttribute(string name) =>
                    GetAttributeValue(name) == null;

                public override string GetAttributeValue(string name) =>
                    Node.GetAttributeValue(name, null);

                public override string OuterHtml => Node.OuterHtml;
                public override string InnerHtml => Node.InnerHtml;
                public override string InnerText => Node.InnerText;

                public override IEnumerable<HtmlObject> ChildElements =>
                    from e in Node.ChildNodes
                    where e.NodeType == HtmlNodeType.Element
                    select _owner.GetPublicObject(e);
            }
        }
    }

    public static class ParsedHtmlExtensions
    {
        public static IEnumerable<T> Links<T>(this ParsedHtml self, Func<string, HtmlObject, T> selector)
        {
            return
                from a in self.QuerySelectorAll("a[href]")
                let href = a.GetAttributeValue("href")
                where !string.IsNullOrWhiteSpace(href)
                select selector(Href(self.BaseUrl, href), a);
        }

        public static IEnumerable<string> Tables(this ParsedHtml self, string selector) =>
            from e in self.QuerySelectorAll(selector ?? "table")
            where "table".Equals(e.Name, StringComparison.OrdinalIgnoreCase)
            select e.OuterHtml;

        static string Href(Uri baseUrl, string href) =>
            baseUrl != null
            ? TryParse.Uri(baseUrl, href)?.OriginalString ?? href
            : href;

        public static IEnumerable<T> GetForms<T>(this ParsedHtml self, string cssSelector, Func<HtmlObject, string, string, string, HtmlFormMethod, ContentType, T> selector) =>
            from form in self.QuerySelectorAll(cssSelector ?? "form[action]")
            where "form".Equals(form.Name, StringComparison.OrdinalIgnoreCase)
            let method = form.GetAttributeValue("method")?.Trim()
            let enctype = form.GetAttributeValue("enctype")?.Trim()
            let action = form.GetAttributeValue("action")
            select selector(form,
                            form.GetAttributeValue("id"),
                            form.GetAttributeValue("name"),
                            action != null ? Href(form.Owner.BaseUrl, action) : action,
                            "post".Equals(method, StringComparison.OrdinalIgnoreCase)
                                ? HtmlFormMethod.Post
                                : HtmlFormMethod.Get,
                            enctype != null ? new ContentType(enctype) : null);

        public static IEnumerable<TForm> FormsWithControls<TControl, TForm>(this ParsedHtml self, string cssSelector, Func<string, HtmlControlType, HtmlInputType, HtmlDisabledFlag, HtmlReadOnlyFlag, string, TControl> controlSelector, Func<string, string, string, HtmlFormMethod, ContentType, string, IEnumerable<TControl>, TForm> formSelector) =>
            self.GetForms(cssSelector, (fe, id, name, action, method, enctype) =>
                formSelector(id, name, action, method, enctype, fe.OuterHtml,
                    fe.GetFormWithControls((ce, cn, ct, it, cd, cro) =>
                        controlSelector(cn, ct, it, cd, cro, ce.OuterHtml))));

        public static IEnumerable<T> GetFormWithControls<T>(this HtmlObject formElement,
            Func<HtmlObject, string, HtmlControlType, HtmlInputType, HtmlDisabledFlag, HtmlReadOnlyFlag, T> selector)
        {
            //
            // Grab all INPUT and SELECT elements belonging to the form.
            //
            // TODO: BUTTON
            // TODO: formaction https://developer.mozilla.org/en-US/docs/Web/HTML/Element/button#attr-formaction
            // TODO: formenctype https://developer.mozilla.org/en-US/docs/Web/HTML/Element/button#attr-formenctype
            // TODO: formmethod https://developer.mozilla.org/en-US/docs/Web/HTML/Element/button#attr-formmethod
            //

            const string @readonly = "readonly";
            const string disabled = "disabled";

            return
                from e in formElement.Owner.QuerySelectorAll("input, select, textarea", formElement)
                let name = e.GetAttributeValue("name")?.Trim() ?? string.Empty
                where name.Length > 0
                let controlType = "select".Equals(e.Name, StringComparison.OrdinalIgnoreCase)
                                ? HtmlControlType.Select
                                : "textarea".Equals(e.Name, StringComparison.OrdinalIgnoreCase)
                                ? HtmlControlType.TextArea
                                : HtmlControlType.Input
                let attrs = new
                {
                    Disabled  = e.GetAttributeValue(disabled)?.Trim(),
                    ReadOnly  = e.GetAttributeValue(@readonly)?.Trim(),
                    InputType = controlType == HtmlControlType.Input
                                ? e.GetAttributeValue("type")?.Trim().Map(HtmlInputType.Parse)
                                // Missing "type" attribute implies "text" since HTML 3.2
                                ?? HtmlInputType.Default
                                : null,
                }
                select selector
                (
                    e,
                    name,
                    controlType,
                    attrs.InputType,
                    disabled.Equals(attrs.Disabled, StringComparison.OrdinalIgnoreCase) ? HtmlDisabledFlag.Disabled : HtmlDisabledFlag.Default,
                    @readonly.Equals(attrs.ReadOnly, StringComparison.OrdinalIgnoreCase) ? HtmlReadOnlyFlag.ReadOnly : HtmlReadOnlyFlag.Default
                );
        }
    }
}