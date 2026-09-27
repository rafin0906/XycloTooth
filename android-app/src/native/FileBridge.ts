import { NativeModules, NativeEventEmitter } from 'react-native';

const { FileBridgeModule } = NativeModules;

export interface PairedDevice {
  name: string;
  address: string;
}

export interface ServiceStatus {
  isRunning: boolean;
  selectedDeviceAddress: string;
  selectedDeviceName: string;
  serverUrl: string;
  incomingDirectory: string;
}

export interface AppSettings {
  serverUrl: string;
  apiToken: string;
  pairedDeviceAddress: string;
  pairedDeviceName: string;
  incomingDirectory: string;
  responseDirectory: string;
  autoUpload: boolean;
  autoSendResponse: boolean;
  retryCount: number;
  retryDelaySeconds: number;
}

export interface TransferRecord {
  id: string;
  filename: string;
  fileSize: number;
  sha256: string;
  state: 'DETECTED' | 'WAITING_FOR_STABILITY' | 'READY' | 'UPLOADING' | 'UPLOADED' | 'WAITING_FOR_RESPONSE' | 'RESPONSE_RECEIVED' | 'BLUETOOTH_SEND_PENDING' | 'BLUETOOTH_SENDING' | 'SENT_TO_PC' | 'FAILED';
  direction: string;
  requestId: string;
  responseFilename?: string;
  retryCount: number;
  lastError?: string;
  createdAt: number;
}

export const FileBridge = {
  startService: (): Promise<boolean> => FileBridgeModule.startService(),
  stopService: (): Promise<boolean> => FileBridgeModule.stopService(),
  getServiceStatus: (): Promise<ServiceStatus> => FileBridgeModule.getServiceStatus(),
  getPairedDevices: (): Promise<PairedDevice[]> => FileBridgeModule.getPairedDevices(),
  selectBluetoothDevice: (address: string, name: string): Promise<boolean> =>
    FileBridgeModule.selectBluetoothDevice(address, name),
  getSettings: (): Promise<AppSettings> => FileBridgeModule.getSettings(),
  saveSettings: (settings: Partial<AppSettings>): Promise<boolean> =>
    FileBridgeModule.saveSettings(settings),
  getRecentTransfers: (limit: number = 50): Promise<TransferRecord[]> =>
    FileBridgeModule.getRecentTransfers(limit),
  retryTransfer: (id: string): Promise<boolean> => FileBridgeModule.retryTransfer(id),
};

export const FileBridgeEmitter = new NativeEventEmitter(FileBridgeModule);
