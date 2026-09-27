import React, { useEffect, useState } from 'react';
import {
  Modal,
  View,
  Text,
  TextInput,
  TouchableOpacity,
  Switch,
  ScrollView,
  StyleSheet,
  Alert,
} from 'react-native';
import { FileBridge, AppSettings } from '../native/FileBridge';

interface SettingsModalProps {
  visible: boolean;
  onClose: () => void;
  onSaved: () => void;
}

export const SettingsModal: React.FC<SettingsModalProps> = ({ visible, onClose, onSaved }) => {
  const [serverUrl, setServerUrl] = useState('');
  const [apiToken, setApiToken] = useState('');
  const [incomingDir, setIncomingDir] = useState('');
  const [responseDir, setResponseDir] = useState('');
  const [autoUpload, setAutoUpload] = useState(true);
  const [autoSendResponse, setAutoSendResponse] = useState(true);
  const [retryCount, setRetryCount] = useState(3);

  useEffect(() => {
    if (visible) {
      FileBridge.getSettings().then((s: AppSettings) => {
        setServerUrl(s.serverUrl);
        setApiToken(s.apiToken);
        setIncomingDir(s.incomingDirectory);
        setResponseDir(s.responseDirectory);
        setAutoUpload(s.autoUpload);
        setAutoSendResponse(s.autoSendResponse);
        setRetryCount(s.retryCount);
      });
    }
  }, [visible]);

  const handleSave = async () => {
    try {
      await FileBridge.saveSettings({
        serverUrl,
        apiToken,
        incomingDirectory: incomingDir,
        responseDirectory: responseDir,
        autoUpload,
        autoSendResponse,
        retryCount: Number(retryCount),
      });
      Alert.alert('Saved', 'Configuration updated successfully.');
      onSaved();
      onClose();
    } catch (e: any) {
      Alert.alert('Error', e?.message || 'Failed to save settings');
    }
  };

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <View style={styles.overlay}>
        <View style={styles.modal}>
          <View style={styles.header}>
            <Text style={styles.title}>Bridge Configuration</Text>
            <TouchableOpacity onPress={onClose} style={styles.closeBtn}>
              <Text style={styles.closeBtnText}>✕</Text>
            </TouchableOpacity>
          </View>

          <ScrollView style={styles.body} showsVerticalScrollIndicator={false}>
            {/* Server URL */}
            <Text style={styles.label}>Server Base URL</Text>
            <TextInput
              style={styles.input}
              value={serverUrl}
              onChangeText={setServerUrl}
              placeholder="http://192.168.1.50:3000"
              placeholderTextColor="#64748B"
              autoCapitalize="none"
              autoCorrect={false}
            />

            {/* API Bearer Token */}
            <Text style={styles.label}>API Bearer Token</Text>
            <TextInput
              style={styles.input}
              value={apiToken}
              onChangeText={setApiToken}
              placeholder="xyclo-secret-token-2026"
              placeholderTextColor="#64748B"
              secureTextEntry
              autoCapitalize="none"
            />

            {/* Incoming Directory */}
            <Text style={styles.label}>Incoming Directory (PC → Android)</Text>
            <TextInput
              style={styles.input}
              value={incomingDir}
              onChangeText={setIncomingDir}
              autoCapitalize="none"
            />

            {/* Response Directory */}
            <Text style={styles.label}>Response Directory (Server Responses)</Text>
            <TextInput
              style={styles.input}
              value={responseDir}
              onChangeText={setResponseDir}
              autoCapitalize="none"
            />

            {/* Auto Upload Toggle */}
            <View style={styles.switchRow}>
              <View>
                <Text style={styles.switchLabel}>Auto-Upload to Server</Text>
                <Text style={styles.switchSublabel}>Automatically upload detected .txt files</Text>
              </View>
              <Switch
                value={autoUpload}
                onValueChange={setAutoUpload}
                trackColor={{ false: '#334155', true: '#38BDF8' }}
              />
            </View>

            {/* Auto Send Response Toggle */}
            <View style={styles.switchRow}>
              <View>
                <Text style={styles.switchLabel}>Auto-Send Response to PC</Text>
                <Text style={styles.switchSublabel}>Automatically stream response.txt back</Text>
              </View>
              <Switch
                value={autoSendResponse}
                onValueChange={setAutoSendResponse}
                trackColor={{ false: '#334155', true: '#38BDF8' }}
              />
            </View>

            {/* Retry Count */}
            <Text style={styles.label}>Max Retry Count</Text>
            <TextInput
              style={styles.input}
              value={String(retryCount)}
              onChangeText={(t) => setRetryCount(parseInt(t, 10) || 0)}
              keyboardType="numeric"
            />
          </ScrollView>

          <TouchableOpacity style={styles.saveBtn} onPress={handleSave}>
            <Text style={styles.saveBtnText}>Save Settings</Text>
          </TouchableOpacity>
        </View>
      </View>
    </Modal>
  );
};

const styles = StyleSheet.create({
  overlay: {
    flex: 1,
    backgroundColor: 'rgba(0,0,0,0.7)',
    justifyContent: 'flex-end',
  },
  modal: {
    backgroundColor: '#0F172A',
    borderTopLeftRadius: 16,
    borderTopRightRadius: 16,
    padding: 20,
    maxHeight: '85%',
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 16,
  },
  title: {
    fontSize: 18,
    fontWeight: '700',
    color: '#F8FAFC',
  },
  closeBtn: {
    padding: 6,
  },
  closeBtnText: {
    color: '#94A3B8',
    fontSize: 18,
  },
  body: {
    marginBottom: 16,
  },
  label: {
    fontSize: 12,
    fontWeight: '600',
    color: '#94A3B8',
    marginBottom: 6,
    marginTop: 10,
  },
  input: {
    backgroundColor: '#1E293B',
    borderRadius: 8,
    padding: 12,
    color: '#F8FAFC',
    borderWidth: 1,
    borderColor: '#334155',
    fontSize: 14,
  },
  switchRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginTop: 14,
    marginBottom: 4,
  },
  switchLabel: {
    fontSize: 14,
    fontWeight: '600',
    color: '#F8FAFC',
  },
  switchSublabel: {
    fontSize: 11,
    color: '#64748B',
    marginTop: 2,
  },
  saveBtn: {
    backgroundColor: '#38BDF8',
    padding: 14,
    borderRadius: 8,
    alignItems: 'center',
  },
  saveBtnText: {
    color: '#0F172A',
    fontWeight: '700',
    fontSize: 15,
  },
});
