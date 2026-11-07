import { Alert } from 'react-native';
import { Host, List, ListItem } from '@expo/ui';
import { router } from 'expo-router';
import { useSession } from '@/auth/session-provider';

export function ProfileScreen() {
  const session = useSession();
  if (session.status !== 'signedIn') return null;
  return (
    <Host style={{ flex: 1 }}>
      <List>
        <ListItem supportingText={session.user.email}>
          {session.user.fullName || 'Agenti account'}
        </ListItem>
        <ListItem supportingText={session.user.role}>Role</ListItem>
        <ListItem
          supportingText={
            session.user.branchId
              ? String(session.user.branchId)
              : 'Not assigned - contact your administrator'
          }
        >
          Branch
        </ListItem>
        <ListItem
          supportingText={session.user.agentId ? String(session.user.agentId) : 'Not assigned'}
        >
          Agent
        </ListItem>
        <ListItem supportingText={new Date(session.expiresAt).toLocaleString()}>
          Session expires
        </ListItem>
        <ListItem supportingText={session.controller.apiUrl}>API server</ListItem>
        {session.user.role !== 'Agent' && (
          <ListItem onPress={() => router.push('/vault')}>Branch vault</ListItem>
        )}
        <ListItem
          supportingText="This device only. Sign in again after expiry."
          onPress={() =>
            Alert.alert('Sign out?', 'Unsaved cash counts will be discarded.', [
              { text: 'Cancel', style: 'cancel' },
              {
                text: 'Sign out',
                style: 'destructive',
                onPress: () => {
                  void session.controller.signOut();
                },
              },
            ])
          }
        >
          Sign out
        </ListItem>
      </List>
    </Host>
  );
}
