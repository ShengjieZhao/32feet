﻿//-----------------------------------------------------------------------
// <copyright file="GattCharacteristic.android.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-20 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using ABluetooth = Android.Bluetooth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InTheHand.Bluetooth
{
    partial class GattCharacteristic
    {
        private readonly ABluetooth.BluetoothGattCharacteristic _characteristic;

        internal GattCharacteristic(GattService service, ABluetooth.BluetoothGattCharacteristic characteristic) : this(service)
        {
            _characteristic = characteristic;
        }

        public static implicit operator ABluetooth.BluetoothGattCharacteristic(GattCharacteristic characteristic)
        {
            return characteristic._characteristic;
        }

        BluetoothUuid GetUuid()
        {
            return _characteristic.Uuid;
        }

        GattCharacteristicProperties GetProperties()
        {
            return (GattCharacteristicProperties)(int)_characteristic.Properties;
        }

        string GetUserDescription()
        {
            return GetManualUserDescription();
        }

        Task<GattDescriptor> PlatformGetDescriptor(BluetoothUuid descriptor)
        {
            var gattDescriptor = _characteristic.GetDescriptor(descriptor);
            if (gattDescriptor is null)
                return Task.FromResult<GattDescriptor>(null);

            return Task.FromResult(new GattDescriptor(this, gattDescriptor));
        }

        async Task<IReadOnlyList<GattDescriptor>> PlatformGetDescriptors()
        {
            List<GattDescriptor> descriptors = new List<GattDescriptor>();

            foreach(var descriptor in _characteristic.Descriptors)
            {
                descriptors.Add(new GattDescriptor(this, descriptor));
            }

            return descriptors;
        }

        Task<byte[]> PlatformGetValue()
        {
            return Task.FromResult(_characteristic.GetValue());
        }

        Task<byte[]> PlatformReadValue()
        {
            TaskCompletionSource<byte[]> tcs = new TaskCompletionSource<byte[]>();

            void handler(object s, CharacteristicEventArgs e)
            {
                if (e.Characteristic == _characteristic)
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.SetResult(_characteristic.GetValue());
                    }

                    Service.Device.Gatt.CharacteristicRead -= handler;
                }
            };

            Service.Device.Gatt.CharacteristicRead += handler;
            bool read = ((ABluetooth.BluetoothGatt)Service.Device.Gatt).ReadCharacteristic(_characteristic);
            return tcs.Task;
        }

        Task PlatformWriteValue(byte[] value, bool requireResponse)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            void handler(object s, CharacteristicEventArgs e)
            {
                if (e.Characteristic == _characteristic)
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.SetResult(e.Status == ABluetooth.GattStatus.Success);
                    }

                    Service.Device.Gatt.CharacteristicWrite -= handler;
                }
            };

            Service.Device.Gatt.CharacteristicWrite += handler;
            bool written = _characteristic.SetValue(value);
            _characteristic.WriteType = requireResponse ? ABluetooth.GattWriteType.Default : ABluetooth.GattWriteType.NoResponse;
            written = ((ABluetooth.BluetoothGatt)Service.Device.Gatt).WriteCharacteristic(_characteristic);
            return tcs.Task;
        }

        void AddCharacteristicValueChanged()
        {
            Service.Device.Gatt.CharacteristicChanged += Gatt_CharacteristicChanged;
        }

        private void Gatt_CharacteristicChanged(object sender, CharacteristicEventArgs e)
        {
            if(e.Characteristic == _characteristic)
                characteristicValueChanged?.Invoke(this, EventArgs.Empty);
        }

        void RemoveCharacteristicValueChanged()
        {
            Service.Device.Gatt.CharacteristicChanged -= Gatt_CharacteristicChanged;
        }

        private async Task PlatformStartNotifications()
        {
            byte[] data;

            if (_characteristic.Properties.HasFlag(ABluetooth.GattProperty.Notify))
                data = ABluetooth.BluetoothGattDescriptor.EnableNotificationValue.ToArray();
            else if (_characteristic.Properties.HasFlag(ABluetooth.GattProperty.Indicate))
                data = ABluetooth.BluetoothGattDescriptor.EnableIndicationValue.ToArray();
            else
                return;

            ((ABluetooth.BluetoothGatt)Service.Device.Gatt).SetCharacteristicNotification(_characteristic, true);
            var descriptor = await GetDescriptorAsync(GattDescriptorUuids.ClientCharacteristicConfiguration);
            await descriptor.WriteValueAsync(data);
        }

        private async Task PlatformStopNotifications()
        {
            ((ABluetooth.BluetoothGatt)Service.Device.Gatt).SetCharacteristicNotification(_characteristic, false);
            var descriptor = await GetDescriptorAsync(GattDescriptorUuids.ClientCharacteristicConfiguration);
            await descriptor.WriteValueAsync(new byte[] { 0, 0 });
        }
    }
}
