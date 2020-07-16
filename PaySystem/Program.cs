using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PaySystem
{
    class Server
    {
        public static string type = "pay";

        public static HttpListener listener;
        public static string url = "http://localhost:8000/";
        
        /// <summary>
        /// All OK message pattern
        /// </summary>
        public class Data
        {   //Successful response
            [JsonProperty("type")]
            public string type;
            [JsonProperty("id")]
            public string id;
            [JsonProperty("attributes")]
            public Card attributes;

            public Data(string type, string id)
            {
                this.type = type;
                this.id = id;
                this.attributes = null;
            }

            public Data(string type, string id, Card card)
            {
                this.type = type;
                this.id = id;
                this.attributes = card;
            }
        }


        /// <summary>
        /// Errors pattern
        /// </summary>
        public class Error
        {   //Something went wrong
            public int status;
            public string title;
            public int code;

            public Error(int status, string title, int code)
            {
                this.status = status;
                this.title = title;
                this.code = code;
            }
        }

        /// <summary>
        /// Payment acceptance pattern
        /// </summary>
        public class Card
        {
            [JsonProperty("number")]
            public string number;
            [JsonProperty("CVVCVC")]
            public int CVVCVC;
            [JsonProperty("year")]
            public int year;
            [JsonProperty("mounth")]
            public int mounth;
            [JsonProperty("URL")]
            public string URL;

            public Card(string number, int CVVCVC, int year, int mounth, string URL)
            {
                this.number = number;
                this.CVVCVC = CVVCVC;
                this.year = year;
                this.mounth = mounth;
                this.URL = URL;
            }
        }

        /// <summary>
        /// Patternt for http-notice
        /// </summary>
        public class Notice
        {
            public class Attributes
            {
                [JsonProperty("number")]
                public string number;

                [JsonProperty("amount")]
                public double amount;

                [JsonProperty("purpose")]
                public string purpose;

                public Attributes(string number, double amount, string purpose)
                {
                    this.number = number;
                    this.amount = amount;
                    this.purpose = purpose;
                }
            }

            [JsonProperty("type")]
            public string type;

            [JsonProperty("id")]
            public string id;

            [JsonProperty("attributes")]
            public Attributes attributes;

            public Notice(string type, Guid id, Attributes attributes)
            {
                this.type = type;
                this.id = id.ToString();
                this.attributes = attributes;
            }
        }

        public class Session
        {   //Stored session
            public static List<Session> list = new List<Session>();
            public Guid sessionId;
            public double amount; //Amount of payment
            public string purpose; //Purpose of payment

            public Session(double amount, string purpose)
            {
                this.amount = amount;
                this.purpose = purpose;
                this.sessionId = Guid.NewGuid();

                LifeEnd(120000);

                list.Add(this);
            }

            public static Session Find(Guid sessionId)
            {
                return list.Find((x) =>
                {
                    if(x.sessionId == sessionId)
                    {
                        return true;
                    }
                    return false;
                });
            }

            //Lifetime limit
            async void LifeEnd(int milliseconds)
            {
                await Task.Delay(milliseconds);
                list.Remove(this);
            }
        }

        // Keep on handling requests
        public static async Task HandleIncomingConnections()
        {
            while (true)
            {
                // Will wait here until we hear from a connection
                HttpListenerContext ctx = await listener.GetContextAsync();

                // Peel out the requests and response objects
                HttpListenerRequest req = ctx.Request;
                HttpListenerResponse resp = ctx.Response;

                int status = 200;
                string statusDescription = "OK";
                string json = "";

                Error[] errors = new Error[1];

                if(req.Url.AbsolutePath == "/" + type)
                {
                    //OK
                    if (req.HttpMethod == "GET")
                    {   //Return sessionid
                        try
                        {
                            //Create new payment session
                            Session session = new Session(Double.Parse(req.QueryString["amount"]), 
                                req.QueryString["purpose"]);
                            json = JsonConvert.SerializeObject(new Data(type, 
                                session.sessionId.ToString()));
                        }
                        catch(Exception e)
                        {
                            //Error: amount or purpose is not exists
                            errors[0] = new Error(400, "Amount or purpose is missing!", 6);
                            json = JsonConvert.SerializeObject(errors);
                            status = 400;
                            statusDescription = "Bad Request";
                            Console.WriteLine(e);
                        }
                    }
                    else if(req.HttpMethod == "POST")
                    {   //Make payment
                        try
                        {
                            //Read request body
                            dynamic request = JsonConvert.DeserializeObject(
                                ReadInputStream(req.InputStream));

                            if(request.type == type)
                            {
                                Session session = Session.Find(Guid.Parse((string) request.id));
                                if (session != null)
                                {
                                    Card card = new Card((string)request.attributes.number, (int)request.attributes.CVVCVC, 
                                        (int)request.attributes.year, (int)request.attributes.mounth, 
                                        (string)request.attributes.URL);

                                    //Сheck the data is full
                                    if (card.number.Length >= 4 && card.CVVCVC >= 100 && card.CVVCVC <= 999 &&
                                        card.mounth >= 1 && card.year >= 0 && card.URL != "")
                                    {
                                        //Check the card number in Luna algorithm
                                        if (SimpleLuna(card.number))
                                        {   

                                            //Payment successful
                                            json = JsonConvert.SerializeObject(new Data(type, 
                                                session.sessionId.ToString()));

                                            //Send http-notice
                                                HttpNotice(card.URL, session.sessionId, session.purpose,
                                            card.number, session.amount);

                                        }
                                        else
                                        {
                                            //Error: the card number is not correct
                                            errors[0] = new Error(400, "The card number is not correct!", 1);
                                            json = JsonConvert.SerializeObject(errors);
                                            status = 400;
                                            statusDescription = "Bad Request";
                                        }
                                    }
                                    else
                                    {
                                        //Error: the card data is not correct
                                        errors[0] = new Error(400, "The card data is not correct!", 2);
                                        json = JsonConvert.SerializeObject(errors);
                                        status = 400;
                                        statusDescription = "Bad Request";
                                    }

                                    //Delete used session
                                    Session.list.Remove(session);
                                }
                                else
                                {
                                    //Error: dead session
                                    errors[0] = new Error(400, "Dead session!", 3);
                                    json = JsonConvert.SerializeObject(errors);
                                    status = 400;
                                    statusDescription = "Bad Request";
                                }
                            }
                            else
                            {
                                //Error: unknown request type
                                errors[0] = new Error(400, "Unknown request type!", 4);
                                json = JsonConvert.SerializeObject(errors);
                                status = 400;
                                statusDescription = "Bad Request";
                            }
                        }
                        catch (Exception e)
                        {
                            if(e.GetType().Name == "UriFormatException")
                            {
                                errors[0] = new Error(400, "Invalid URL!", 4);
                                json = JsonConvert.SerializeObject(errors);
                                status = 400;
                                statusDescription = "Bad Request";
                            }
                            else if(e.GetType().Name == "FormatException")
                            {
                                errors[0] = new Error(400, "Bad Request", 7);
                                json = JsonConvert.SerializeObject(errors);
                                status = 400;
                                statusDescription = "Bad Request";
                            }
                            else
                            {
                                //Error: 500 - Internal Server Error
                                status = 500;
                                statusDescription = "Internal Server Error";
                                Console.WriteLine(e);
                            }
                        }
                    }
                    else
                    {
                        //Error: unknown method
                        errors[0] = new Error(400, "Unknown method!", 5);
                        json = JsonConvert.SerializeObject(errors);
                        status = 400;
                        statusDescription = "Bad Request";
                    }
                }
                else
                {
                    //Error: resource not found
                    status = 404;
                    statusDescription = "Not Found";
                }

                byte[] data = Encoding.UTF8.GetBytes(json);
                resp.StatusCode = status;
                resp.StatusDescription = statusDescription;
                resp.ContentType = "application/vnd.api+json";
                resp.ContentEncoding = Encoding.UTF8;
                resp.ContentLength64 = data.LongLength;

                // Write out to the response stream (asynchronously), then close it
                await resp.OutputStream.WriteAsync(data, 0, data.Length);
                resp.Close();
            }
        }


        /// <summary>
        /// Realisation of Simple Luna algorithm
        /// </summary>
        /// <param name="number">Card number</param>
        /// <returns>Is number correct?</returns>
        static bool SimpleLuna(string number)
        {
            int parity = number.Length % 2;
            int sum = 0;
            for (int i = 0; i < number.Length; i++)
            {
                int n = Int32.Parse(number[i].ToString());
                if (i % 2 == parity)
                {
                    n *= 2;
                    n = n > 9 ? n - 9 : n;
                    sum += n;
                }
                else
                {
                    sum += n;
                }
            }
            return sum % 10 == 0;
        }

        /// <summary>
        /// Read the client request stream
        /// </summary>
        /// <param name="Request">Request stream</param>
        /// <returns>Client request body on string format</returns>
        public static string ReadInputStream(Stream Request)
        {
            byte[] data = new byte[1024];
            Request.Read(data, 0, 1024);

            string converted = Encoding.UTF8.GetString(data, 0, data.Length);

            return converted;
        }

        /// <summary>
        /// Asynchronously sends an HTTP-notification to the server
        /// </summary>
        /// <param name="url">server url</param>
        /// <param name="id">sessionid</param>
        /// <param name="number">card number</param>
        /// <param name="amount">payment amount</param>
        static async void HttpNotice(string url, Guid id, string purpose, string number, double amount)
        {
            try
            {
                WebRequest request = WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/vnd.api+json";

                string json = JsonConvert.SerializeObject(new Notice("notice", id, new Notice.Attributes(
                    number, amount, purpose)));
                byte[] data = Encoding.UTF8.GetBytes(json);
                request.ContentLength = data.Length;


                await Task.Run(() =>
                {
                    using (Stream dataStream = request.GetRequestStream())
                    {
                        dataStream.WriteAsync(data, 0, data.Length);
                    }
                });
            }
            catch(WebException we)
            {
                //Ignore it!
            }
            
        }

        static void Main(string[] args)
        {
            // Create a Http server and start listening for incoming connections
            listener = new HttpListener();
            listener.Prefixes.Add(url);
            listener.Start();
            Console.WriteLine("Listening for connections on {0}", url);

            // Handle requests
            Task listenTask = HandleIncomingConnections();
            listenTask.GetAwaiter().GetResult();

            // Close the listener
            listener.Close();
        }
    }
}
