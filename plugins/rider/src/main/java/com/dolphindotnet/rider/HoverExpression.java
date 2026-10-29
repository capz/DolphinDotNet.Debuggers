package com.dolphindotnet.rider;

/** Select only variable/member paths; calls and indexers must never run on hover. */
final class HoverExpression {
    record Range(int start, int end) { }
    static Range at(CharSequence text, int offset) {
        if (offset < 0 || offset >= text.length() || !part(text.charAt(offset))) return null;
        int start = offset, end = offset + 1;
        while (start > 0 && part(text.charAt(start - 1))) start--;
        while (end < text.length() && part(text.charAt(end))) end++;
        if (end < text.length() && text.charAt(end) == '(') return null;
        while (true) {
            int dot = start - 1;
            while (dot >= 0 && horizontalSpace(text.charAt(dot))) dot--;
            if (dot < 0 || text.charAt(dot) != '.') break;
            int previous = dot - 1;
            while (previous >= 0 && horizontalSpace(text.charAt(previous))) previous--;
            // Do not reinterpret GetObject().length or items[0].length as this.length.
            if (previous < 0 || !part(text.charAt(previous))) return null;
            start = previous;
            while (start > 0 && part(text.charAt(start - 1))) start--;
        }
        String expression = text.subSequence(start, end).toString();
        if (expression.length() > 256 || !expression.matches("@?[\\p{L}_][\\p{L}\\p{Nd}_]*(\\h*\\.\\h*@?[\\p{L}_][\\p{L}\\p{Nd}_]*)*")) return null;
        return new Range(start, end);
    }
    private static boolean part(char value) { return value == '_' || value == '@' || Character.isLetterOrDigit(value); }
    private static boolean horizontalSpace(char value) { return value == ' ' || value == '\t'; }
}
