import React, { useEffect, useState } from 'react';
import {
  Modal,
  View,
  Text,
  TouchableOpacity,
  FlatList,
  StyleSheet,
  ActivityIndicator,
} from 'react-native';
import { FileBridge, PairedDevice } from '../native/FileBridge';

interface DevicePickerModalProps {
  visible: boolean;
  selectedAddress: string;
  onClose: () => void;
  onSelect: (device: PairedDevice) => void;
}

export const DevicePickerModal: React.FC<DevicePickerModalProps> = ({
  visible,
  selectedAddress,
  onClose,
  onSelect,
}) => {
  const [devices, setDevices] = useState<PairedDevice[]>([]);
  const [loading, setLoading] = useState(false);

  const loadDevices = async () => {
    setLoading(true);
    try {
      const list = await FileBridge.getPairedDevices();
      setDevices(list);
    } catch (e) {
      console.warn('Failed to load paired devices', e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (visible) {
      loadDevices();
    }
  }, [visible]);

  return (
    <Modal visible={visible} animationType="slide" transparent onRequestClose={onClose}>
      <View style={styles.overlay}>
        <View style={styles.modal}>
          <View style={styles.header}>
            <Text style={styles.title}>Select Paired Windows PC</Text>
            <TouchableOpacity onPress={onClose} style={styles.closeBtn}>
              <Text style={styles.closeBtnText}>✕</Text>
            </TouchableOpacity>
          </View>

          <Text style={styles.subtitle}>
            Ensure your Windows PC is paired with this phone in Bluetooth settings.
          </Text>

          {loading ? (
            <ActivityIndicator size="large" color="#38BDF8" style={{ marginVertical: 30 }} />
          ) : (
            <FlatList
              data={devices}
              keyExtractor={(item) => item.address}
              ListEmptyComponent={
                <Text style={styles.emptyText}>
                  No paired Bluetooth devices found. Please pair in Android Bluetooth settings first.
                </Text>
              }
              renderItem={({ item }) => {
                const isSelected = item.address.toLowerCase() === selectedAddress.toLowerCase();
                return (
                  <TouchableOpacity
                    style={[styles.deviceItem, isSelected && styles.deviceItemSelected]}
                    onPress={() => onSelect(item)}
                  >
                    <View>
                      <Text style={[styles.deviceName, isSelected && styles.deviceNameSelected]}>
                        {item.name}
                      </Text>
                      <Text style={styles.deviceAddress}>{item.address}</Text>
                    </View>
                    {isSelected ? <Text style={styles.checkIcon}>✓</Text> : null}
                  </TouchableOpacity>
                );
              }}
            />
          )}

          <TouchableOpacity style={styles.refreshBtn} onPress={loadDevices}>
            <Text style={styles.refreshBtnText}>🔄 Refresh Devices</Text>
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
    maxHeight: '75%',
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 8,
  },
  title: {
    fontSize: 18,
    fontWeight: '700',
    color: '#F8FAFC',
  },
  subtitle: {
    fontSize: 13,
    color: '#64748B',
    marginBottom: 16,
  },
  closeBtn: {
    padding: 6,
  },
  closeBtnText: {
    color: '#94A3B8',
    fontSize: 18,
  },
  deviceItem: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    backgroundColor: '#1E293B',
    padding: 14,
    borderRadius: 8,
    marginBottom: 8,
    borderWidth: 1,
    borderColor: '#334155',
  },
  deviceItemSelected: {
    borderColor: '#38BDF8',
    backgroundColor: '#1E3A8A',
  },
  deviceName: {
    fontSize: 15,
    fontWeight: '600',
    color: '#F8FAFC',
  },
  deviceNameSelected: {
    color: '#38BDF8',
  },
  deviceAddress: {
    fontSize: 12,
    color: '#94A3B8',
    marginTop: 2,
  },
  checkIcon: {
    color: '#38BDF8',
    fontSize: 18,
    fontWeight: 'bold',
  },
  emptyText: {
    color: '#94A3B8',
    textAlign: 'center',
    marginVertical: 20,
  },
  refreshBtn: {
    backgroundColor: '#334155',
    padding: 12,
    borderRadius: 8,
    alignItems: 'center',
    marginTop: 12,
  },
  refreshBtnText: {
    color: '#F8FAFC',
    fontWeight: '600',
  },
});
