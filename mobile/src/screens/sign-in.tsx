import { useRef, useState } from 'react';
import { Keyboard, Text } from 'react-native';
import { Button, Host } from '@expo/ui';
import { useSession } from '@/auth/session-provider';
import { ApiError } from '@/api/client';
import { ErrorNotice, Message, Screen } from '@/components/content';
import { Field } from '@/components/field';
import { type } from '@/theme';

export function SignInScreen() {
  const session = useSession();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const submitting = useRef(false);

  async function signIn() {
    if (submitting.current) return;
    if (!email.trim() || !password) {
      setError('Enter your email and password.');
      return;
    }
    submitting.current = true;
    setPending(true);
    setError(null);
    Keyboard.dismiss();
    try {
      await session.controller.signIn(email, password);
      setPassword('');
    } catch (cause) {
      setError(
        cause instanceof ApiError
          ? cause.message
          : 'Sign-in could not be completed. Please try again.',
      );
    } finally {
      submitting.current = false;
      setPending(false);
    }
  }

  return (
    <Screen>
      <Message title="Your branch, on your iPhone">
        Use your existing Agenti account. Banking data stays on your existing server.
      </Message>
      <Text selectable style={type.caption}>
        {session.controller.apiUrl}
      </Text>
      {session.error && <ErrorNotice error={session.error} />}
      <Field
        label="Email"
        value={email}
        onChangeText={setEmail}
        keyboardType="email-address"
        autoComplete="email"
        autoCapitalize="none"
        autoCorrect={false}
        editable={!pending}
      />
      <Field
        label="Password"
        value={password}
        onChangeText={setPassword}
        secureTextEntry
        autoComplete="current-password"
        autoCapitalize="none"
        autoCorrect={false}
        editable={!pending}
        returnKeyType="go"
        onSubmitEditing={() => {
          void signIn();
        }}
      />
      {error && <ErrorNotice error={error} />}
      <Host matchContents>
        <Button
          label={pending ? 'Signing in...' : 'Sign in'}
          disabled={pending}
          onPress={() => {
            void signIn();
          }}
        />
      </Host>
    </Screen>
  );
}
