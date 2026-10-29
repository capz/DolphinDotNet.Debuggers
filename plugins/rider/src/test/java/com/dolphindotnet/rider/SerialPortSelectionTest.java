package com.dolphindotnet.rider;

import org.junit.jupiter.api.Test;
import javax.swing.JComboBox;
import javax.swing.SwingUtilities;
import static org.junit.jupiter.api.Assertions.*;

class SerialPortSelectionTest {
    @Test void discoveredPortsCanBeSelectedWithoutChoosingOneAutomatically() throws Exception {
        SwingUtilities.invokeAndWait(() -> {
            var picker = new JComboBox<String>(); picker.setEditable(true);
            SerialPortSelection.update(picker, new String[]{"COM3", "COM7"});
            assertEquals(2, picker.getItemCount());
            assertEquals("", picker.getEditor().getItem());
            assertEquals("", picker.getSelectedItem());
            picker.setSelectedIndex(1);
            assertEquals("COM7", picker.getEditor().getItem());
        });
    }

    @Test void refreshPreservesTypedAndSavedPortsIncludingDisconnectedDevices() throws Exception {
        SwingUtilities.invokeAndWait(() -> {
            var picker = new JComboBox<String>(); picker.setEditable(true);
            for (var current : new String[]{"COM7", "COM99", "/dev/cu.usbserial-gecko"}) {
                picker.getEditor().setItem(current);
                SerialPortSelection.update(picker, new String[]{"COM3", "COM7"});
                assertEquals(current, picker.getEditor().getItem());
                assertEquals(current, picker.getSelectedItem());
                SerialPortSelection.update(picker, new String[0]);
                assertEquals(current, picker.getEditor().getItem());
            }
        });
    }
}
