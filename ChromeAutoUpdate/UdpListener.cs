﻿using SimpleJson;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ChromeAutoUpdate
{
    public class UdpListener
    {
        /// <summary>
        /// 用于UDP接收的网络服务类
        /// </summary>
        private UdpClient udpcRecv;

        private rsa UserRsa = new rsa();


        /// <summary>
        /// 消息格式版本
        /// </summary>
        private int version = 1;


        private int udp_port = 20172;

        private string id = "";

        /// <summary>
        /// 本地ip
        /// </summary>
        private IPAddress local_ip;


        private SortedDictionary<node_key, node> node_list;

        private Hashtable node_table = new Hashtable();



        public UdpListener()
        {
            //获取Host Name
            var name = Dns.GetHostName();
            //从当前Host Name解析IP地址，筛选IPv4地址是本机的内网IP地址。
            local_ip = Dns.GetHostEntry(name).AddressList.Where(i => i.AddressFamily == AddressFamily.InterNetwork).FirstOrDefault();

            //不存在就创建
            if (!File.Exists("Private.xml") || !File.Exists("Public.xml"))
            {
                UserRsa.RSAKey("Private.xml", "Public.xml");
                this.id = UserRsa.sha1(UserRsa.readFile("Public.xml"));
            }
            else
            {
                this.id = UserRsa.sha1(UserRsa.readFile("Public.xml"));
            }

            if(!Directory.Exists("node"))
            {
                Directory.CreateDirectory("node");
            }

        }

        public void StartListener()
        {
            IPEndPoint localIpep = new IPEndPoint(IPAddress.Any, this.udp_port); // 本机IP和监听端口号
            udpcRecv = new UdpClient(localIpep);
            
            thrRecv = new Thread(ReceiveMessage);
            thrRecv.Start();

            log("UDP监听器已成功启动");

            node_list = new SortedDictionary<node_key, node>();

            IPEndPoint remoteIpep = new IPEndPoint(IPAddress.Broadcast, this.udp_port); // 发送到的IP地址和端口号


            if (File.Exists("node/node.json"))
            {
                log("读取node/node.json");
                loadNode();
            }

            while (true)
            {
                if (node_table.Count == 0)
                {
                    this.ping(new IPEndPoint(IPAddress.Broadcast, this.udp_port));
                    log("广播局域网");
                }
                else
                {
                    log("已有用户"+ node_table.Count.ToString());
                }

                Hashtable node_table_tmp = (Hashtable) node_table.Clone();

                foreach (node n in node_table_tmp.Values)
                {
                    log("循环ping:"+ n.ip.ToString());
                    if(node_table.Count > 100)
                    {
                        this.ping(n.ip);
                        Thread.Sleep(1000);
                    }
                    else
                    {
                        this.find_node(n.ip, "888888888888888888888888888888888888");
                    }
                }

                Thread.Sleep(5000);

                foreach (node n in node_table_tmp.Values)
                {
                     if( node.unixtime() - n.last_time > 60)
                    {
                        node_table.Remove(n.uid);
                        log("用户下线：" + n.ip.ToString());
                    }
                }


                node_table_tmp = (Hashtable)node_table.Clone();
                JsonArray list_share_node = new JsonArray();

                foreach (node ns in node_table_tmp.Values)
                {
                    JsonObject jsonObject = new JsonObject();
                    jsonObject["uid"] = ns.uid;
                    jsonObject["ip"] = ns.ip.Address.ToString();
                    jsonObject["port"] = ns.ip.Port.ToString();
                    list_share_node.Add(jsonObject);
                }

                if(node_table_tmp.Values.Count > 0)
                {
                    string json = SimpleJson.SimpleJson.SerializeObject(list_share_node);
                    writeFile("node/node.json", json);
                }
            }

        }

        public void writeFile(string path, string txt)
        {
            try
            {
                FileStream file = new FileStream(path, FileMode.Create);
                StreamWriter sw = new StreamWriter(file);
                sw.WriteLine(txt);
                sw.Close();
                file.Close();
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// 读取文件
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>
        public string readFile(string filename)
        {
            StreamReader reader = new StreamReader(filename);
            string str = reader.ReadToEnd();
            reader.Close();
            return str.Trim();
        }


        /// <summary>
        /// 加载节点
        /// </summary>
        public void loadNode()
        {
            string node_json = readFile("node/node.json");

            var nodes = SimpleJson.SimpleJson.DeserializeObject(node_json.Trim());

            JsonArray file_nodes = (JsonArray)nodes;

            foreach (JsonObject n in file_nodes)
            {
                //添加节点
                if (!node_table.ContainsKey(n["uid"].ToString()))
                {
                    node_table.Add(n["uid"].ToString(),
                    new node(n["uid"].ToString(),
                    new IPEndPoint(IPAddress.Parse(n["ip"].ToString()),
                    int.Parse(n["port"].ToString())
                    )));
                    log("find新节点" + n["uid"].ToString() + n["ip"].ToString());
                }
                else
                {
                    log("find重复节点");
                }
            }

        }


        /// <summary>
        /// 线程：不断监听UDP报文
        /// </summary>
        Thread thrRecv;

        /// <summary>
        /// 接收数据
        /// </summary>
        private void ReceiveMessage()
        {
            IPEndPoint remoteIpep = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                try
                {
                    byte[] bytRecv = udpcRecv.Receive(ref remoteIpep);

                    if(remoteIpep.Address.ToString() == local_ip.ToString())
                    {
                        continue;
                    }

                    string message = Encoding.Unicode.GetString(bytRecv, 0, bytRecv.Length);

                    log(string.Format("{0}:{1}[{2}]",remoteIpep.Address,remoteIpep.Port, message));

                    //处理消息
                    var json = (IDictionary<string, object>)SimpleJson.SimpleJson.DeserializeObject(message);
                    string type = "";
                    string uid = "";
                    if (json.ContainsKey("type"))
                        type = (string)json["type"];


                    if (json.ContainsKey("uid"))
                        uid = (string)json["uid"];

                    if (json.ContainsKey("type"))
                    {
                        switch (type)
                        {
                            case "ping":
                                log("收到ping");
                                this.pong(remoteIpep);
                                if(json.ContainsKey("uid") && !File.Exists(@"node\"+ (string)json["uid"]))
                                {
                                    this.who(remoteIpep);
                                }
                                else
                                {
                                    log("已知用户");
                                }
                                break;
                            case "pong":
                                log("收到pong");

                                if (node_table.ContainsKey(uid))
                                {
                                    log("重复");
                                    node_table[uid] = new node(uid, remoteIpep);
                                }
                                else
                                {
                                    log("不重复" + node_list.Count);
                                    node_table.Add(uid, new node(uid, remoteIpep));
                                }


                                break;
                            case "who":
                                log("收到who");
                                this.me(remoteIpep);
                                break;
                            case "me":
                                log("收到who");
                                if(json.ContainsKey("msg"))
                                {
                                    //todo 证书有效性验证
                                    this.UserRsa.writeFile(@"node\" + this.UserRsa.sha1(json["msg"].ToString().Trim()), (string)json["msg"]);
                                }
                                break;
                            case "find_node":
                                if(json.ContainsKey("msg"))
                                {
                                    this.reply_node(remoteIpep, json["msg"].ToString());
                                }
                                break;
                            case "reply_node":
                                if (json.ContainsKey("msg"))
                                {
                                    try
                                    {
                                        JsonArray nodes = (JsonArray)json["msg"];
                                        foreach(JsonObject n in nodes)
                                        {
                                            //添加节点
                                            if(!node_table.ContainsKey(n["uid"].ToString()))
                                            {
                                                node_table.Add(n["uid"].ToString(),
                                                new node(n["uid"].ToString(),
                                                new IPEndPoint(IPAddress.Parse(n["ip"].ToString()),
                                                (int)n["port"]
                                                )));
                                                log("find新节点"+ n["uid"].ToString() + n["ip"].ToString());
                                            }
                                            else
                                            {
                                                log("find重复节点");
                                            }
                                        }
                                    }
                                    catch
                                    {
                                        continue;
                                    }
                                }
                                break;
                            default:
                                log("收到未知消息");
                                break;
                        }
                    }
                    
                }
                catch (Exception ex)
                {
                    log( ex.Message);
                    break;
                }
            }
        }


        public static bool log(string msg)
        {
            if (!File.Exists("debug"))
                return false;
            string filename = DateTime.Now.ToString("yyyy-MM-dd") + ".udplog.log";
            try
            {
                using (StreamWriter sw = new StreamWriter(@filename, true))//覆盖模式写入
                {
                    sw.WriteLine(DateTime.Now.ToLongTimeString() + " udp:" + msg);
                    sw.Close();
                }
            }
            catch
            {
                return false;
            }
            return true;
        }


        public void ping(IPEndPoint remoteIpep)
        {

            var obj = new Dictionary<string, object>();
            obj["type"] = "ping";
            obj["msg"] = "";
            obj["version"] = this.version;
            obj["uid"] = this.id;

            string json = SimpleJson.SimpleJson.SerializeObject(obj);

            byte[] sendbytes = Encoding.Unicode.GetBytes(json);

            udpcRecv.Send(sendbytes, sendbytes.Length, remoteIpep);
        }


        /// <summary>
        /// 回应ping
        /// </summary>
        /// <param name="remoteIpep"></param>
        public void pong(IPEndPoint remoteIpep)
        {

            var obj = new Dictionary<string, object>();
            obj["type"] = "pong";
            obj["msg"] = "";
            obj["version"] = this.version;
            obj["uid"] = this.id;

            string json = SimpleJson.SimpleJson.SerializeObject(obj);

            byte[] sendbytes = Encoding.Unicode.GetBytes(json);

            log("发送pong："+ remoteIpep.ToString());
            udpcRecv.Send(sendbytes, sendbytes.Length, remoteIpep);
        }


        /// <summary>
        /// 询问身份(交换证书，后面可以加入昵称、地区、简介这些
        /// </summary>
        /// <param name="remoteIpep"></param>
        public void who(IPEndPoint remoteIpep)
        {
            var obj = new Dictionary<string, object>();
            obj["type"] = "who";
            obj["msg"] = "";
            obj["version"] = this.version;
            obj["uid"] = this.id;

            string json = SimpleJson.SimpleJson.SerializeObject(obj);

            byte[] sendbytes = Encoding.Unicode.GetBytes(json);

            udpcRecv.Send(sendbytes, sendbytes.Length, remoteIpep);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="remoteIpep"></param>
        public void me(IPEndPoint remoteIpep)
        {
            var obj = new Dictionary<string, object>();
            obj["type"] = "me";
            obj["msg"] = this.UserRsa.readFile("Public.xml");
            obj["version"] = this.version;
            obj["uid"] = this.id;

            string json = SimpleJson.SimpleJson.SerializeObject(obj);

            byte[] sendbytes = Encoding.Unicode.GetBytes(json);

            udpcRecv.Send(sendbytes, sendbytes.Length, remoteIpep);
        }



        /// <summary>
        /// 查找节点
        /// </summary>
        /// <param name="remoteIpep"></param>
        /// <param name="uid"></param>
        public void reply_node(IPEndPoint remoteIpep, string uid)
        {
            var obj = new Dictionary<string, object>();
            obj["type"] = "reply_node";
            obj["version"] = this.version;
            obj["uid"] = this.id;

            Hashtable share_node = new Hashtable();

            Hashtable node_table_tmp = (Hashtable)node_table.Clone();

            foreach (node n in node_table_tmp.Values)
            {
                if (share_node.Values.Count < 10)
                {
                    share_node.Add(n.uid, n);
                }
                else
                {
                    foreach (node ns in share_node.Values)
                    {
                        if (distance(ns.uid, uid) < distance(n.uid, uid))
                        {
                            share_node.Remove(n.uid);
                            share_node.Add(n.uid, n);
                        }
                    }
                }
            }

            JsonArray list_share_node = new JsonArray();

            foreach (node ns in share_node.Values)
            {
                JsonObject jsonObject = new JsonObject();
                jsonObject["uid"] = ns.uid;
                jsonObject["ip"] = ns.ip.Address.ToString();
                jsonObject["port"] = ns.ip.Port.ToString();
                list_share_node.Add(jsonObject);
            }

            obj["msg"] = list_share_node;

            string json = SimpleJson.SimpleJson.SerializeObject(obj);

            byte[] sendbytes = Encoding.Unicode.GetBytes(json);
            log("发送reply_node");
            udpcRecv.Send(sendbytes, sendbytes.Length, remoteIpep);
        }


        /// <summary>
        /// 查找节点
        /// </summary>
        /// <param name="remoteIpep"></param>
        /// <param name="uid"></param>
        public void find_node(IPEndPoint remoteIpep, string uid)
        {
            var obj = new Dictionary<string, object>();
            obj["type"] = "find_node";
            obj["msg"] = uid;
            obj["version"] = this.version;
            obj["uid"] = this.id;

            string json = SimpleJson.SimpleJson.SerializeObject(obj);

            byte[] sendbytes = Encoding.Unicode.GetBytes(json);

            log("发送find_node");
            udpcRecv.Send(sendbytes, sendbytes.Length, remoteIpep);
        }


        /// <summary>
        /// 用户距离
        /// </summary>
        /// <returns></returns>
        public int distance(string a,string b)
        {
            int distance = 0;

            for (int i = 0; i < a.Length && i < b.Length;i++)
            {
                if(a.Substring(i) != b.Substring(i))
                {
                    distance++;
                }
            }

            return distance;
        }
    }
}
