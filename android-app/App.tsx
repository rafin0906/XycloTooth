import React, { useEffect, useState, useCallback } from 'react';
import {
  SafeAreaView,
  StatusBar,
  StyleSheet,
  Text,
  View,
  TouchableOpacity,
  FlatList,
  RefreshControl,
  Alert,
} from 'react-native';
import { FileBridge, ServiceStatus, TransferRecord, PairedDevice } from './src/native/FileBridge';
import { StatusCard } from './src/components/StatusCard';
import { DevicePickerModal } from './src/components/DevicePickerModal';
import { SettingsModal } from './src/components/SettingsModal';

export default function App() {
  const [serviceStatus, setServiceStatus] = useState<ServiceStatus | null>(null);
  const [transfers, setTransfers] = useState<TransferRecord[]>([]);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [showDevicePicker, setShowDevicePicker] = useState(false);
  const [showSettings, setShowSettings] = useState(false);

  const fetchStatus = useCallback(async () => {
    try {
      const status = await FileBridge.getServiceStatus();
      setServiceStatus(status);
      const list = await FileBridge.getRecentTransfers(20);
      setTransfers(list);
    } catch (e) {
      console.warn('Error fetching status', e);
    }
  }, []);

  useEffect(() => {
    fetchStatus();
    const interval = setInterval(fetchStatus, 3000);
    return () => clearInterval(interval);
  }, [fetchStatus]);

  const onRefresh = async () => {
    setIsRefreshing(true);
    await fetchStatus();
    setIsRefreshing(false);
  };

  const toggleService = async () => {
    try {
      if (serviceStatus?.isRunning) {
        await FileBridge.stopService();
      } else {
        await FileBridge.startService();
      }
      fetchStatus();
    } catch (e: any) {
      Alert.alert('Service Error', e?.message || 'Failed to toggle service');
    }
  };

  const handleDeviceSelected = async (device: PairedDevice) => {
    try {
      await FileBridge.selectBluetoothDevice(device.address, device.name);
      setShowDevicePicker(false);
      fetchStatus();
      Alert.alert('PC Selected', `Configured target PC: ${device.name}`);
    } catch (e: any) {
      Alert.alert('Error', e?.message || 'Failed to select device');
    }
  };

  const handleRetry = async (id: string) => {
    try {
      await FileBridge.retryTransfer(id);
      fetchStatus();
    } catch (e: any) {
      Alert.alert('Retry Failed', e?.message);
    }
  };

  const getStatusBadgeStyle = (state: string) => {
    switch (state) {
      case 'SENT_TO_PC':
        return { backgroundColor: '#064E3B', color: '#34D399' };
      case 'UPLOADING':
      case 'BLUETOOTH_SENDING':
        return { backgroundColor: '#1E3A8A', color: '#60A5FA' };
      case 'FAILED':
        return { backgroundColor: '#7F1D1D', color: '#F87171' };
      default:
        return { backgroundColor: '#334155', color: '#94A3B8' };
    }
  };

  return (
    <SafeAreaView style={styles.container}>
      <StatusBar barStyle="light-content" backgroundColor="#0F172A" />

      {/* Header */}
      <View style={styles.header}>
        <View>
          <Text style={styles.appTitle}>XYCLOTOOTH</Text>
          <Text style={styles.appSubtitle}>Automated Text File Bridge</Text>
        </View>

        <TouchableOpacity
          style={[styles.serviceToggleBtn, serviceStatus?.isRunning ? styles.btnRunning : styles.btnStopped]}
          onPress={toggleService}
        >
          <Text style={styles.serviceToggleText}>
            {serviceStatus?.isRunning ? '⏹ Stop' : '▶ Start'}
          </Text>
        </TouchableOpacity>
      </View>

      <FlatList
        data={transfers}
        keyExtractor={(item) => item.id}
        refreshControl={
          <RefreshControl refreshing={isRefreshing} onRefresh={onRefresh} tintColor="#38BDF8" />
        }
        ListHeaderComponent={
          <View style={styles.dashboardSection}>
            {/* Status Cards */}
            <TouchableOpacity onPress={() => setShowDevicePicker(true)} activeOpacity={0.8}>
              <StatusCard
                title="Bluetooth PC Connection"
                subtitle={serviceStatus?.selectedDeviceName || 'No PC Selected'}
                statusText={serviceStatus?.selectedDeviceAddress ? 'Configured' : 'Select PC'}
                statusType={serviceStatus?.selectedDeviceAddress ? 'success' : 'warning'}
                info={serviceStatus?.selectedDeviceAddress || 'Tap to select paired Windows PC'}
              />
            </TouchableOpacity>

            <StatusCard
              title="Folder Monitoring"
              subtitle={serviceStatus?.isRunning ? 'Monitoring Active' : 'Service Inactive'}
              statusText={serviceStatus?.isRunning ? 'Active' : 'Offline'}
              statusType={serviceStatus?.isRunning ? 'success' : 'neutral'}
              info={serviceStatus?.incomingDirectory}
            />

            <StatusCard
              title="Remote Server"
              subtitle={serviceStatus?.serverUrl || 'Not configured'}
              statusText="Ready"
              statusType="success"
            />

            <View style={styles.sectionHeader}>
              <Text style={styles.sectionTitle}>Recent Activity & Transfers</Text>
              <Text style={styles.itemCount}>{transfers.length} items</Text>
            </View>
          </View>
        }
        ListEmptyComponent={
          <View style={styles.emptyContainer}>
            <Text style={styles.emptyText}>No transfers recorded yet.</Text>
            <Text style={styles.emptySubtext}>
              Drop a .txt file on your PC to trigger the automated pipeline.
            </Text>
          </View>
        }
        renderItem={({ item }) => {
          const badgeStyle = getStatusBadgeStyle(item.state);
          return (
            <View style={styles.transferItem}>
              <View style={styles.transferHeader}>
                <Text style={styles.transferFilename} numberOfLines={1}>
                  {item.filename}
                </Text>
                <View style={[styles.stateBadge, { backgroundColor: badgeStyle.backgroundColor }]}>
                  <Text style={[styles.stateBadgeText, { color: badgeStyle.color }]}>
                    {item.state}
                  </Text>
                </View>
              </View>

              <Text style={styles.transferMeta}>
                Size: {item.fileSize} B • {new Date(item.createdAt).toLocaleTimeString()}
              </Text>

              {item.responseFilename ? (
                <Text style={styles.responseInfo}>
                  ↳ Response: {item.responseFilename}
                </Text>
              ) : null}

              {item.lastError ? (
                <View style={styles.errorRow}>
                  <Text style={styles.errorText} numberOfLines={2}>
                    Error: {item.lastError}
                  </Text>
                  <TouchableOpacity style={styles.retryBtn} onPress={() => handleRetry(item.id)}>
                    <Text style={styles.retryBtnText}>Retry</Text>
                  </TouchableOpacity>
                </View>
              ) : null}
            </View>
          );
        }}
      />

      {/* Bottom Controls */}
      <View style={styles.bottomBar}>
        <TouchableOpacity style={styles.actionBtn} onPress={() => setShowDevicePicker(true)}>
          <Text style={styles.actionBtnText}>📱 Select PC</Text>
        </TouchableOpacity>

        <TouchableOpacity style={styles.actionBtn} onPress={() => setShowSettings(true)}>
          <Text style={styles.actionBtnText}>⚙️ Settings</Text>
        </TouchableOpacity>
      </View>

      {/* Modals */}
      <DevicePickerModal
        visible={showDevicePicker}
        selectedAddress={serviceStatus?.selectedDeviceAddress || ''}
        onClose={() => setShowDevicePicker(false)}
        onSelect={handleDeviceSelected}
      />

      <SettingsModal
        visible={showSettings}
        onClose={() => setShowSettings(false)}
        onSaved={fetchStatus}
      />
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: '#0F172A',
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: 20,
    paddingVertical: 14,
    borderBottomWidth: 1,
    borderBottomColor: '#1E293B',
  },
  appTitle: {
    fontSize: 20,
    fontWeight: '800',
    color: '#38BDF8',
    letterSpacing: 0.5,
  },
  appSubtitle: {
    fontSize: 12,
    color: '#94A3B8',
    marginTop: 2,
  },
  serviceToggleBtn: {
    paddingHorizontal: 16,
    paddingVertical: 8,
    borderRadius: 8,
  },
  btnRunning: {
    backgroundColor: '#EF4444',
  },
  btnStopped: {
    backgroundColor: '#10B981',
  },
  serviceToggleText: {
    color: '#FFFFFF',
    fontWeight: '700',
    fontSize: 13,
  },
  dashboardSection: {
    padding: 16,
  },
  sectionHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginTop: 16,
    marginBottom: 8,
  },
  sectionTitle: {
    fontSize: 15,
    fontWeight: '700',
    color: '#F8FAFC',
  },
  itemCount: {
    fontSize: 12,
    color: '#64748B',
  },
  transferItem: {
    backgroundColor: '#1E293B',
    marginHorizontal: 16,
    marginBottom: 10,
    borderRadius: 10,
    padding: 14,
    borderWidth: 1,
    borderColor: '#334155',
  },
  transferHeader: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  transferFilename: {
    fontSize: 15,
    fontWeight: '600',
    color: '#F8FAFC',
    flex: 1,
    marginRight: 10,
  },
  stateBadge: {
    paddingHorizontal: 8,
    paddingVertical: 3,
    borderRadius: 6,
  },
  stateBadgeText: {
    fontSize: 10,
    fontWeight: '700',
  },
  transferMeta: {
    fontSize: 12,
    color: '#64748B',
    marginTop: 6,
  },
  responseInfo: {
    fontSize: 12,
    color: '#34D399',
    marginTop: 4,
    fontWeight: '500',
  },
  errorRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginTop: 8,
    backgroundColor: '#2D1B1F',
    padding: 8,
    borderRadius: 6,
  },
  errorText: {
    color: '#F87171',
    fontSize: 11,
    flex: 1,
  },
  retryBtn: {
    backgroundColor: '#EF4444',
    paddingHorizontal: 10,
    paddingVertical: 4,
    borderRadius: 4,
    marginLeft: 8,
  },
  retryBtnText: {
    color: '#FFFFFF',
    fontSize: 11,
    fontWeight: '700',
  },
  emptyContainer: {
    alignItems: 'center',
    padding: 40,
  },
  emptyText: {
    color: '#94A3B8',
    fontSize: 15,
    fontWeight: '600',
  },
  emptySubtext: {
    color: '#64748B',
    fontSize: 12,
    textAlign: 'center',
    marginTop: 6,
  },
  bottomBar: {
    flexDirection: 'row',
    padding: 12,
    backgroundColor: '#1E293B',
    borderTopWidth: 1,
    borderTopColor: '#334155',
    gap: 10,
  },
  actionBtn: {
    flex: 1,
    backgroundColor: '#334155',
    paddingVertical: 12,
    borderRadius: 8,
    alignItems: 'center',
  },
  actionBtnText: {
    color: '#F8FAFC',
    fontWeight: '600',
    fontSize: 14,
  },
});
