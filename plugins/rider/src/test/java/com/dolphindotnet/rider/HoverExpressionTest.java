package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class HoverExpressionTest {
    @Test void snakePrivateFieldIsAStandaloneHoverExpression() {
        String line = "int remaining=Capacity-length;";
        var range = HoverExpression.at(line, line.indexOf("length") + 2);
        assertNotNull(range);
        assertEquals("length", line.substring(range.start(), range.end()));
    }
    @Test void memberPathsRetainTheirReceiver() {
        for (String expression : new String[]{"this.length", "box.Next.Count", "this . @length"}) {
            var range = HoverExpression.at(expression, expression.length() - 2);
            assertNotNull(range);
            assertEquals(expression, expression.substring(range.start(), range.end()));
        }
    }
    @Test void callsIndexersOperatorsAndInvalidOffsetsAreNotReinterpreted() {
        for (String expression : new String[]{"GetBox().length", "boxes[0].length", ".length"})
            assertNull(HoverExpression.at(expression, expression.length() - 2));
        assertNull(HoverExpression.at("GetLength()", 3));
        assertNull(HoverExpression.at("GetLength ()", 3));
        assertNull(HoverExpression.at("length + 1", 7));
        assertNull(HoverExpression.at("length", -1));
        assertNull(HoverExpression.at("length", 6));
        assertNull(HoverExpression.at("9field", 2));
    }
}
