import { NativeTabs } from 'expo-router/native-tabs';

export default function TabsLayout() {
  return (
    <NativeTabs>
      <NativeTabs.Trigger name="(home)">
        <NativeTabs.Trigger.Icon sf="square.grid.2x2" />
        <NativeTabs.Trigger.Label>Dashboard</NativeTabs.Trigger.Label>
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="agents">
        <NativeTabs.Trigger.Icon sf="person.2" />
        <NativeTabs.Trigger.Label>Agents</NativeTabs.Trigger.Label>
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="sessions">
        <NativeTabs.Trigger.Icon sf="calendar" />
        <NativeTabs.Trigger.Label>Sessions</NativeTabs.Trigger.Label>
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="counts">
        <NativeTabs.Trigger.Icon sf="banknote" />
        <NativeTabs.Trigger.Label>Counts</NativeTabs.Trigger.Label>
      </NativeTabs.Trigger>
      <NativeTabs.Trigger name="profile">
        <NativeTabs.Trigger.Icon sf="person.crop.circle" />
        <NativeTabs.Trigger.Label>Profile</NativeTabs.Trigger.Label>
      </NativeTabs.Trigger>
    </NativeTabs>
  );
}
