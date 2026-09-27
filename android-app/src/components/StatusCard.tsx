import React from 'react';
import { View, Text, StyleSheet } from 'react-native';

interface StatusCardProps {
  title: string;
  subtitle: string;
  statusText: string;
  statusType: 'success' | 'warning' | 'error' | 'neutral';
  info?: string;
}

export const StatusCard: React.FC<StatusCardProps> = ({
  title,
  subtitle,
  statusText,
  statusType,
  info,
}) => {
  const getDotColor = () => {
    switch (statusType) {
      case 'success':
        return '#10B981';
      case 'warning':
        return '#F59E0B';
      case 'error':
        return '#EF4444';
      default:
        return '#64748B';
    }
  };

  return (
    <View style={styles.card}>
      <View style={styles.header}>
        <Text style={styles.title}>{title.toUpperCase()}</Text>
        <View style={styles.statusBadge}>
          <View style={[styles.dot, { backgroundColor: getDotColor() }]} />
          <Text style={[styles.statusText, { color: getDotColor() }]}>{statusText}</Text>
        </View>
      </View>
      <Text style={styles.subtitle} numberOfLines={1} ellipsizeMode="tail">
        {subtitle}
      </Text>
      {info ? (
        <Text style={styles.info} numberOfLines={2} ellipsizeMode="middle">
          {info}
        </Text>
      ) : null}
    </View>
  );
};

const styles = StyleSheet.create({
  card: {
    backgroundColor: '#1E293B',
    borderRadius: 10,
    padding: 14,
    marginBottom: 10,
    borderWidth: 1,
    borderColor: '#334155',
  },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: 6,
  },
  title: {
    fontSize: 11,
    fontWeight: '700',
    color: '#94A3B8',
    letterSpacing: 0.5,
  },
  statusBadge: {
    flexDirection: 'row',
    alignItems: 'center',
  },
  dot: {
    width: 8,
    height: 8,
    borderRadius: 4,
    marginRight: 6,
  },
  statusText: {
    fontSize: 12,
    fontWeight: '600',
  },
  subtitle: {
    fontSize: 15,
    fontWeight: '700',
    color: '#F8FAFC',
  },
  info: {
    fontSize: 12,
    color: '#64748B',
    marginTop: 4,
  },
});
