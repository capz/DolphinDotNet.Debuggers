package com.dolphindotnet.rider;

import javax.swing.DefaultComboBoxModel;
import javax.swing.JComboBox;

/** Refresh discovered choices without changing the user's device selection. */
final class SerialPortSelection {
    static void update(JComboBox<String> picker, String[] ports) {
        // Read at completion: the user may have typed a port or switched targets
        // while enumeration was running. Never auto-select an unrelated device.
        var current = picker.getEditor().getItem();
        picker.setModel(new DefaultComboBoxModel<>(ports));
        picker.setSelectedItem(current);
    }
}
