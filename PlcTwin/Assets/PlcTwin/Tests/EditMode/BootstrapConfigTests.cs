using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace PlcTwin.Tests
{
    public class BootstrapConfigTests
    {
        [Serializable] class Mc { public string ip; public int port, monitorTimer250ms, pollMs; }
        [Serializable] class Mb { public string ip; public int port, unitId, pollMs, writeTableBase, readTableBase; }
        [Serializable] class Map { public string inBits, inWords, outBits, outWords; }
        [Serializable] class Cfg
        {
            public string _help, transport;
            public Mc mc3e; public Mb modbus; public Map map;
            public int connectTimeoutMs, timeoutMs;
            public bool panelVisible;
        }

        [Test]
        public void DefaultConfigParsesWithJsonUtility()
        {
            var path = Path.Combine(Application.dataPath, "StreamingAssets", "plc_twin_config.json");
            var c = JsonUtility.FromJson<Cfg>(File.ReadAllText(path));
            Assert.AreEqual("mc3e", c.transport);
            Assert.AreEqual("192.168.3.39", c.mc3e.ip);
            Assert.AreEqual(5010, c.mc3e.port);
            Assert.AreEqual(4, c.mc3e.monitorTimer250ms);
            Assert.AreEqual(100, c.mc3e.pollMs);
            Assert.AreEqual("127.0.0.1", c.modbus.ip);
            Assert.AreEqual(502, c.modbus.port);
            Assert.AreEqual(1, c.modbus.unitId);
            Assert.AreEqual(200, c.modbus.pollMs);
            Assert.AreEqual(0, c.modbus.writeTableBase);
            Assert.AreEqual(100, c.modbus.readTableBase);
            Assert.AreEqual(3000, c.connectTimeoutMs);
            Assert.AreEqual(1500, c.timeoutMs);
            Assert.AreEqual("D0", c.map.inBits);
            Assert.AreEqual("D40", c.map.inWords);
            Assert.AreEqual("D100", c.map.outBits);
            Assert.AreEqual("D140", c.map.outWords);
            Assert.IsTrue(c.panelVisible);
            Assert.IsNotEmpty(c._help);
            Assert.GreaterOrEqual(c.timeoutMs, c.mc3e.monitorTimer250ms * 250 + 500);
        }
    }
}
