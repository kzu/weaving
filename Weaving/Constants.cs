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

        Only use named colors from this list:
        
        •	black
        •	maroon
        •	green
        •	olive
        •	navy
        •	purple
        •	teal
        •	silver
        •	grey
        •	red
        •	lime
        •	yellow
        •	blue
        •	fuchsia
        •	aqua
        •	white
        •	grey0
        •	navyblue
        •	darkblue
        •	blue3
        •	blue3_1
        •	blue1
        •	darkgreen
        •	deepskyblue4
        •	deepskyblue4_1
        •	deepskyblue4_2
        •	dodgerblue3
        •	dodgerblue2
        •	green4
        •	springgreen4
        •	turquoise4
        •	deepskyblue3
        •	deepskyblue3_1
        •	dodgerblue1
        •	green3
        •	springgreen3
        •	darkcyan
        •	lightseagreen
        •	deepskyblue2
        •	deepskyblue1
        •	green3_1
        •	springgreen3_1
        •	springgreen2
        •	cyan3
        •	darkturquoise
        •	turquoise2
        •	green1
        •	springgreen2_1
        •	springgreen1
        •	mediumspringgreen
        •	cyan2
        •	cyan1
        •	darkred
        •	deeppink4
        •	purple4
        •	purple4_1
        •	purple3
        •	blueviolet
        •	orange4
        •	grey37
        •	mediumpurple4
        •	slateblue3
        •	slateblue3_1
        •	royalblue1
        •	chartreuse4
        •	darkseagreen4
        •	paleturquoise4
        •	steelblue
        •	steelblue3
        •	cornflowerblue
        •	chartreuse3
        •	darkseagreen4_1
        •	cadetblue
        •	cadetblue_1
        •	skyblue3
        •	steelblue1
        •	chartreuse3_1
        •	palegreen3
        •	seagreen3
        •	aquamarine3
        •	mediumturquoise
        •	steelblue1_1
        •	chartreuse2
        •	seagreen2
        •	seagreen1
        •	seagreen1_1
        •	aquamarine1
        •	darkslategray2
        •	darkred_1
        •	deeppink4_1
        •	darkmagenta
        •	darkmagenta_1
        •	darkviolet
        •	purple_1
        •	orange4_1
        •	lightpink4
        •	plum4
        •	mediumpurple3
        •	mediumpurple3_1
        •	slateblue1
        •	yellow4
        •	wheat4
        •	grey53
        •	lightslategrey
        •	mediumpurple
        •	lightslateblue
        •	yellow4_1
        •	darkolivegreen3
        •	darkseagreen
        •	lightskyblue3
        •	lightskyblue3_1
        •	skyblue2
        •	chartreuse2_1
        •	darkolivegreen3_1
        •	palegreen3_1
        •	darkseagreen3
        •	darkslategray3
        •	skyblue1
        •	chartreuse1
        •	lightgreen
        •	lightgreen_1
        •	palegreen1
        •	aquamarine1_1
        •	darkslategray1
        •	red3
        •	deeppink4_2
        •	mediumvioletred
        •	magenta3
        •	darkviolet_1
        •	purple_2
        •	darkorange3
        •	indianred
        •	hotpink3
        •	mediumorchid3
        •	mediumorchid
        •	mediumpurple2
        •	darkgoldenrod
        •	lightsalmon3
        •	rosybrown
        •	grey63
        •	mediumpurple2_1
        •	mediumpurple1
        •	gold3
        •	darkkhaki
        •	navajowhite3
        •	grey69
        •	lightsteelblue3
        •	lightsteelblue
        •	yellow3
        •	darkolivegreen3_2
        •	darkseagreen3_1
        •	darkseagreen2
        •	lightcyan3
        •	lightskyblue1
        •	greenyellow
        •	darkolivegreen2
        •	palegreen1_1
        •	darkseagreen2_1
        •	darkseagreen1
        •	paleturquoise1
        •	red3_1
        •	deeppink3
        •	deeppink3_1
        •	magenta3_1
        •	magenta3_2
        •	magenta2
        •	darkorange3_1
        •	indianred_1
        •	hotpink3_1
        •	hotpink2
        •	orchid
        •	mediumorchid1
        •	orange3
        •	lightsalmon3_1
        •	lightpink3
        •	pink3
        •	plum3
        •	violet
        •	gold3_1
        •	lightgoldenrod3
        •	tan
        •	mistyrose3
        •	thistle3
        •	plum2
        •	yellow3_1
        •	khaki3
        •	lightgoldenrod2
        •	lightyellow3
        •	grey84
        •	lightsteelblue1
        •	yellow2
        •	darkolivegreen1
        •	darkolivegreen1_1
        •	darkseagreen1_1
        •	honeydew2
        •	lightcyan1
        •	red1
        •	deeppink2
        •	deeppink1
        •	deeppink1_1
        •	magenta2_1
        •	magenta1
        •	orangered1
        •	indianred1
        •	indianred1_1
        •	hotpink
        •	hotpink_1
        •	mediumorchid1_1
        •	darkorange
        •	salmon1
        •	lightcoral
        •	palevioletred1
        •	orchid2
        •	orchid1
        •	orange1
        •	sandybrown
        •	lightsalmon1
        •	lightpink1
        •	pink1
        •	plum1
        •	gold1
        •	lightgoldenrod2_1
        •	lightgoldenrod2_2
        •	navajowhite1
        •	mistyrose1
        •	thistle1
        •	yellow1
        •	lightgoldenrod1
        •	khaki1
        •	wheat1
        •	cornsilk1
        •	grey100
        •	grey3
        •	grey7
        •	grey11
        •	grey15
        •	grey19
        •	grey23
        •	grey27
        •	grey30
        •	grey35
        •	grey39
        •	grey42
        •	grey46
        •	grey50
        •	grey54
        •	grey58
        •	grey62
        •	grey66
        •	grey70
        •	grey74
        •	grey78
        •	grey82
        •	grey85
        •	grey89
        •	grey93
        """;
}
