using System.Text.RegularExpressions;

namespace DolphinDotNet.Debugger;

/// <summary>Resolve visible variables and verified fields without executing target code.</summary>
public sealed class ReadOnlyExpressionInspector(RspClient client, SourceAddressMap symbols)
{
    public async Task<InspectedValue> ReadAsync(string expression, NativeStackFrame frame,
        bool topFrame, uint lookup, CancellationToken token = default)
    {
        expression = expression.Trim();
        if (expression.Length is 0 or > 256 || !Regex.IsMatch(expression,
                @"^@?[\p{L}_][\p{L}\p{Nd}_]*(\s*\.\s*@?[\p{L}_][\p{L}\p{Nd}_]*)*$"))
            throw new ArgumentException("Only read-only variable and field names are supported.");
        var path = expression.Split('.').Select(part => part.Trim().TrimStart('@')).ToArray();
        if (path.Length > ObjectInspector.MaximumDepth + 1)
            throw new ArgumentException("Field inspection depth limit exceeded.");
        var visible = symbols.FindVariables(lookup);
        var matches = visible.Where(variable => variable.Name == path[0]).ToArray();
        var inspector = new ObjectInspector(client, symbols);
        InspectedValue value;
        var index = 1;
        if (matches.Length == 1)
            value = await inspector.ReadAsync(matches[0], frame, topFrame, token).ConfigureAwait(false);
        else if (matches.Length > 1)
            throw new ArgumentException("The variable name is ambiguous in this frame.");
        else
        {
            // An unqualified instance field such as SnakeGame.length means this.length.
            var receiver = visible.Where(variable => variable.Name == "this" && variable.Group == "Arguments").ToArray();
            if (receiver.Length != 1) throw new ArgumentException("The name is not available in this frame.");
            value = await inspector.ReadAsync(receiver[0], frame, topFrame, token).ConfigureAwait(false);
            index = 0;
        }
        for (; index < path.Length; index++)
        {
            if (value.Children is not ObjectView view)
                throw new ArgumentException("The value has no inspectable fields: " + value.Value);
            var fields = view.Layout.Fields.Where(field => field.Name == path[index]).ToArray();
            if (fields.Length != 1) throw new ArgumentException("The field name is unavailable or ambiguous: " + path[index]);
            value = await inspector.ReadFieldAsync(view, fields[0], token).ConfigureAwait(false);
        }
        return value;
    }
}
