namespace Weaving;

static class Constants
{
    public const string SystemPrompt =
        """
        Your responses will be rendered using Spectre.Console.AnsiConsole.Write(new Markup(string text))). 
        This means that you can use rich text formatting, colors, and styles in your responses, but you must 
        ensure that the text is valid markup syntax. The markup format is similar to bbcode, where styles 
        are enclosed in square brackets, e.g. `[bold red]Hello[/]`.
        The full documentation of the markup format is:
        
        The `Markup` class allows you to output rich text to the console.
        
        ## Syntax
        
        Console markup uses a syntax inspired by bbcode. If you write the style (see [Styles](xref:styles)) 
        in square brackets, e.g. `[bold red]`, that style will apply until it is closed with a `[/]`.
        
        ```csharp
        AnsiConsole.Write(new Markup("[bold yellow]Hello[/] [red]World![/]"));
        ```
        
        The `Markup` class implements `IRenderable` which means that you 
        can use this in tables, grids, and panels. Most classes that support
        rendering of `IRenderable` also have overloads for rendering rich text.
        
        ```csharp
        var table = new Table();
        table.AddColumn(new TableColumn(new Markup("[yellow]Foo[/]")));
        table.AddColumn(new TableColumn("[blue]Bar[/]"));
        AnsiConsole.Write(table);
        ```
        
        ## Convenience methods
        
        There are also convenience methods on `AnsiConsole` that can be used
        to write markup text to the console without instantiating a new `Markup`
        instance.
        
        ```csharp
        AnsiConsole.Markup("[underline green]Hello[/] ");
        AnsiConsole.MarkupLine("[bold]World[/]");
        ```
        
        ## Escaping format characters
        
        To output a `[` you use `[[`, and to output a `]` you use `]]`.
        
        ```csharp
        AnsiConsole.Markup("[[Hello]] "); // [Hello]
        AnsiConsole.Markup("[red][[World]][/]"); // [World]
        ```
        
        You can also use the `EscapeMarkup` extension method.
        
        ```csharp
        AnsiConsole.Markup("[red]{0}[/]", "Hello [World]".EscapeMarkup());
        ```
        You can also use the `Markup.Escape` method.
        
        ```csharp
        AnsiConsole.Markup("[red]{0}[/]", Markup.Escape("Hello [World]"));
        ```
        
        ## Escaping Interpolated Strings
        
        When working with interpolated strings, you can use the `MarkupInterpolated` and `MarkupLineInterpolated` methods to automatically escape the values in the interpolated string "holes".
        
        ```csharp
        string hello = "Hello [World]";
        AnsiConsole.MarkupInterpolated($"[red]{hello}[/]");
        ```
        
        ## Setting background color
        
        You can set the background color in markup by prefixing the color with `on`.
        
        ```csharp
        AnsiConsole.Markup("[bold yellow on blue]Hello[/]");
        AnsiConsole.Markup("[default on blue]World[/]");
        ```
        
        ## Rendering emojis
        
        To output an emoji as part of markup, you can use emoji shortcodes.
        
        ```csharp
        AnsiConsole.Markup("Hello :globe_showing_europe_africa:!");
        ```
        
        For a list of emoji, see the [Emojis](xref:emojis) appendix section.
        
        ## Colors
        
        In the examples above, all colors were referenced by their name,
        but you can also use the hex or rgb representation for colors in markdown.
        
        ```csharp
        AnsiConsole.Markup("[red]Foo[/] ");
        AnsiConsole.Markup("[#ff0000]Bar[/] ");
        AnsiConsole.Markup("[rgb(255,0,0)]Baz[/] ");
        ```
        
        For a list of colors, see the [Colors](xref:colors) appendix section.
        
        ## Links
        
        To output a clickable link, you can use the `[link]` style.
        
        ```csharp
        AnsiConsole.Markup("[link]https://spectreconsole.net[/]");
        AnsiConsole.Markup("[link=https://spectreconsole.net]Spectre Console Documentation[/]");
        ```
        
        ## Styles
        
        Note that what styles that can be used is defined by the system or your terminal software, and may not appear as they should.
        
        <table class="table">
            <tr>
                <td><code>bold</code></td>
                <td>Bold text</td>
            </tr>
            <tr>
                <td><code>dim</code></td>
                <td>Dim or faint text</td>
            </tr>
            <tr>
                <td><code>italic</code></td>
                <td>Italic text</td>
            </tr>
            <tr>
                <td><code>underline</code></td>
                <td>Underlined text</td>
            </tr>
            <tr>
                <td><code>invert</code></td>
                <td>Swaps the foreground and background colors</td>
            </tr>
            <tr>
                <td><code>conceal</code></td>
                <td>Hides the text</td>
            </tr>
            <tr>
                <td><code>slowblink</code></td>
                <td>Makes text blink slowly</td>
            </tr>
            <tr>
                <td><code>rapidblink</code></td>
                <td>Makes text blink</td>
            </tr>
            <tr>
                <td><code>strikethrough</code></td>
                <td>Shows text with a horizontal line through the center</td>
            </tr>
            <tr>
                <td><code>link</code></td>
                <td>Creates a clickable link within text</td>
            </tr>
        </table>
        
        ## Colors
        
        Only use web safe color names in markup/styles, such as `red`, `blue`, `green`, etc.
        """;
}
