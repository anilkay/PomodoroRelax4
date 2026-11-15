using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.TagHelpers;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace PomodoroRelax4.TagHelpers;

/// <summary>
/// A labelled text input with its validation message, as the account and admin forms use it:
/// <c>&lt;form-field asp-for="Input.Email" type="email" label="Email" placeholder="…" /&gt;</c>.
/// Other attributes (placeholder, autocomplete, …) land on the input.
/// The Tailwind classes here are found through the TagHelpers entry in tailwind.config.js.
/// </summary>
[HtmlTargetElement("form-field", Attributes = "asp-for", TagStructure = TagStructure.WithoutEndTag)]
public class FormFieldTagHelper : TagHelper
{
    private const string LabelClass = "block text-fg/80 text-sm mb-1";
    private const string InputClass = "w-full px-3 py-2 rounded bg-field border border-fg/10 text-fg placeholder-fg/50 focus:outline-none focus:ring-2 focus:ring-fg/40";
    private const string ValidationClass = "text-danger text-xs mt-1 block";

    private readonly IHtmlGenerator _generator;

    public FormFieldTagHelper(IHtmlGenerator generator) => _generator = generator;

    [HtmlAttributeName("asp-for")]
    public ModelExpression For { get; set; } = default!;

    public string Label { get; set; } = "";

    public string Type { get; set; } = "text";

    /// <summary>Spacing under the field; the last one before the submit button takes mb-6.</summary>
    public string WrapperClass { get; set; } = "mb-4";

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = default!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        // A password is never written back into the page
        var input = Type == "password"
            ? _generator.GeneratePassword(ViewContext, For.ModelExplorer, For.Name, value: null, htmlAttributes: null)
            : _generator.GenerateTextBox(ViewContext, For.ModelExplorer, For.Name, For.Model, format: null, htmlAttributes: null);

        output.TagName = "input";
        output.TagMode = TagMode.SelfClosing;
        output.Attributes.SetAttribute("type", Type);
        output.Attributes.SetAttribute("class", InputClass);
        output.MergeAttributes(input);

        var wrapper = new TagBuilder("div");
        wrapper.AddCssClass(WrapperClass);

        var label = new TagBuilder("label");
        label.AddCssClass(LabelClass);
        if (input.Attributes.TryGetValue("id", out var id))
            label.Attributes["for"] = id;
        label.InnerHtml.Append(Label);

        output.PreElement.AppendHtml(wrapper.RenderStartTag()).AppendHtml(label);

        var validation = _generator.GenerateValidationMessage(ViewContext, For.ModelExplorer, For.Name,
            message: null, tag: null, htmlAttributes: new { @class = ValidationClass });
        if (validation is not null)
            output.PostElement.AppendHtml(validation);
        output.PostElement.AppendHtml(wrapper.RenderEndTag());
    }
}
