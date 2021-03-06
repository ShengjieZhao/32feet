﻿//-----------------------------------------------------------------------
// <copyright file="BluetoothRemoteGATTServer.android.cs" company="In The Hand Ltd">
//   Copyright (c) 2018-20 In The Hand Ltd, All rights reserved.
//   This source code is licensed under the MIT License - see License.txt
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ABluetooth = Android.Bluetooth;
using Android.Runtime;
using System.Runtime.InteropServices;

namespace InTheHand.Bluetooth
{
    partial class BluetoothRemoteGATTServer
    {
        private ABluetooth.BluetoothGatt _gatt;
        private ABluetooth.BluetoothGattCallback _gattCallback;
        
        private void PlatformInit()
        {
            _gattCallback = new GattCallback(this);
            _gatt = ((ABluetooth.BluetoothDevice)Device).ConnectGatt(Android.App.Application.Context, false, _gattCallback);
        }

        public static implicit operator ABluetooth.BluetoothGatt(BluetoothRemoteGATTServer gatt)
        {
            return gatt._gatt;
        }

        internal event EventHandler<ConnectionStateEventArgs> ConnectionStateChanged;
        internal event EventHandler<CharacteristicEventArgs> CharacteristicChanged;
        internal event EventHandler<CharacteristicEventArgs> CharacteristicRead;
        internal event EventHandler<CharacteristicEventArgs> CharacteristicWrite;
        internal event EventHandler<DescriptorEventArgs> DescriptorRead;
        internal event EventHandler<DescriptorEventArgs> DescriptorWrite;
        internal event EventHandler<GattEventArgs> ServicesDiscovered;
        private bool _servicesDiscovered = false;

        internal class GattCallback : ABluetooth.BluetoothGattCallback
        {
            private readonly BluetoothRemoteGATTServer _owner;

            internal GattCallback(BluetoothRemoteGATTServer owner)
            {
                _owner = owner;
            }

            public override void OnConnectionStateChange(ABluetooth.BluetoothGatt gatt, ABluetooth.GattStatus status, ABluetooth.ProfileState newState)
            {
                System.Diagnostics.Debug.WriteLine($"ConnectionStateChanged {status} {newState}");
                _owner.ConnectionStateChanged?.Invoke(_owner, new ConnectionStateEventArgs { Status = status, State = newState });
                if (newState == ABluetooth.ProfileState.Connected)
                {
                    if(!_owner._servicesDiscovered)
                        gatt.DiscoverServices();
                }
                else
                {
                    _owner.Device.OnGattServerDisconnected();
                }
            }

            public override void OnCharacteristicRead(ABluetooth.BluetoothGatt gatt, ABluetooth.BluetoothGattCharacteristic characteristic, ABluetooth.GattStatus status)
            {
                System.Diagnostics.Debug.WriteLine($"CharacteristicRead {characteristic.Uuid} {status}");
                _owner.CharacteristicRead?.Invoke(_owner, new CharacteristicEventArgs { Characteristic = characteristic, Status = status });
            }

            public override void OnCharacteristicWrite(ABluetooth.BluetoothGatt gatt, ABluetooth.BluetoothGattCharacteristic characteristic, ABluetooth.GattStatus status)
            {
                System.Diagnostics.Debug.WriteLine($"CharacteristicWrite {characteristic.Uuid} {status}");
                _owner.CharacteristicWrite?.Invoke(_owner, new CharacteristicEventArgs { Characteristic = characteristic, Status = status });
            }

            public override void OnCharacteristicChanged(ABluetooth.BluetoothGatt gatt, ABluetooth.BluetoothGattCharacteristic characteristic)
            {
                System.Diagnostics.Debug.WriteLine($"CharacteristicChanged {characteristic.Uuid}");
                _owner.CharacteristicChanged?.Invoke(_owner, new CharacteristicEventArgs { Characteristic = characteristic });
            }

            public override void OnDescriptorRead(ABluetooth.BluetoothGatt gatt, ABluetooth.BluetoothGattDescriptor descriptor, ABluetooth.GattStatus status)
            {
                System.Diagnostics.Debug.WriteLine($"DescriptorRead {descriptor.Uuid} {status}");
                _owner.DescriptorRead?.Invoke(_owner, new DescriptorEventArgs { Descriptor = descriptor, Status = status });
            }

            public override void OnDescriptorWrite(ABluetooth.BluetoothGatt gatt, ABluetooth.BluetoothGattDescriptor descriptor, ABluetooth.GattStatus status)
            {
                System.Diagnostics.Debug.WriteLine($"DescriptorWrite {descriptor.Uuid} {status}");
                _owner.DescriptorWrite?.Invoke(_owner, new DescriptorEventArgs { Descriptor = descriptor, Status = status });
            }

            public override void OnServicesDiscovered(ABluetooth.BluetoothGatt gatt, ABluetooth.GattStatus status)
            {
                System.Diagnostics.Debug.WriteLine($"ServicesDiscovered {status}");
                _owner._servicesDiscovered = true;
                _owner.ServicesDiscovered?.Invoke(_owner, new GattEventArgs { Status = status });
            }
        }

        bool GetConnected()
        {
            return Bluetooth._manager.GetConnectionState(Device, ABluetooth.ProfileType.Gatt) == ABluetooth.ProfileState.Connected;
        }

        private async Task<bool> WaitForServiceDiscovery()
        {
            if (_servicesDiscovered)
                return true;

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            void handler(object s, GattEventArgs e)
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetResult(true);

                    ServicesDiscovered -= handler;
                }
            };

            ServicesDiscovered += handler;
            return await tcs.Task;
        }

        Task PlatformConnect()
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            void handler(object s, ConnectionStateEventArgs e)
            {
                switch (e.Status)
                {
                    case ABluetooth.GattStatus.Success:
                        tcs.SetResult(e.State == ABluetooth.ProfileState.Connected);
                        break;

                    default:
                        tcs.SetResult(false);
                        break;
                }

                ConnectionStateChanged -= handler;
            }

            ConnectionStateChanged += handler;
            bool success = _gatt.Connect();
            if (success)
            {
                if (Connected)
                    return Task.FromResult(true);

                return tcs.Task;
            }
            else
            {
                return Task.FromException(new OperationCanceledException());
            }
        }

        void PlatformDisconnect()
        {
            _gatt.Disconnect();
        }

        async Task<GattService> PlatformGetPrimaryService(BluetoothUuid service)
        {
            await WaitForServiceDiscovery();

            ABluetooth.BluetoothGattService nativeService = _gatt.GetService(service);
            
            return nativeService is null ? null : new GattService(Device, nativeService);
        }

        async Task<List<GattService>> PlatformGetPrimaryServices(BluetoothUuid? service)
        {
            var services = new List<GattService>();

            await WaitForServiceDiscovery();

            foreach (var serv in _gatt.Services)
            {
                // if a service was specified only add if service uuid is a match
                if (serv.Type == ABluetooth.GattServiceType.Primary && (!service.HasValue || service.Value == serv.Uuid))
                {
                    services.Add(new GattService(Device, serv));
                }
            }

            return services;
        }
    }

    internal class GattEventArgs : EventArgs
    {
        public ABluetooth.GattStatus Status
        {
            get; internal set;
        }
    }

    internal class ConnectionStateEventArgs : GattEventArgs
    {
        public ABluetooth.ProfileState State
        {
            get; internal set;
        }
    }

    internal class CharacteristicEventArgs : GattEventArgs
    {
        public ABluetooth.BluetoothGattCharacteristic Characteristic
        {
            get; internal set;
        }
    }

    internal class DescriptorEventArgs : GattEventArgs
    {
        public ABluetooth.BluetoothGattDescriptor Descriptor
        {
            get; internal set;
        }
    }
}
