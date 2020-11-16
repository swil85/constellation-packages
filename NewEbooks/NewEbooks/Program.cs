using Constellation;
using Constellation.Package;
using HtmlAgilityPack;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Timers;

namespace NewEbooks
{
    public class Program : PackageBase
    {
        static void Main(string[] args)
        {
            PackageHost.Start<Program>(args);
        }

        public override void OnStart()
        {
            PackageHost.WriteInfo("Package starting - IsRunning: {0} - IsConnected: {1}", PackageHost.IsRunning, PackageHost.IsConnected);
            Traitement();

            Timer syncTimer = new Timer(1000 * 3600 * 3);
            syncTimer.Elapsed += (source, e) =>
            {
                Traitement();
            };
            syncTimer.AutoReset = true;
            syncTimer.Enabled = true;
        }

        /// <summary>
        /// Traitement des livres
        /// </summary>
        private void Traitement()
        {
            try
            {
                PackageHost.WriteInfo("Récupération livre deja traités");
                var database = new MongoClient("mongodb+srv://Swil:200385@myhome-3eosj.gcp.mongodb.net/test?retryWrites=true").GetDatabase("MyHome");
                var ebookCol = database.GetCollection<Livre>("Ebooks");
                var dejaConnu = ebookCol.Find(_ => true).ToList();

                // Dico contenant la liste des livres avec lien vers fp
                Dictionary<string, string> ebooks;
                string html;
                var htmlDoc = new HtmlDocument();
                var httpClient = new HttpClient();

                new List<string>(2)
                {
                    "https://go.1001ebooks.com/romans-gratuit/erotique-epub/",
                    "https://go.1001ebooks.com/romans-gratuit/romance-epub/"
                }
                .ForEach(url =>
                {
                    ebooks = new Dictionary<string, string>(10);

                    PackageHost.WriteInfo($"Traitement de {url}.");
                    html = httpClient.GetStringAsync(url).ConfigureAwait(false).GetAwaiter().GetResult();
                    htmlDoc.LoadHtml(html);

                    PackageHost.WriteInfo("Récupération des livres + lien vers fp.");
                    htmlDoc.DocumentNode.Descendants("div").Where(node => node.GetAttributeValue("class", "").Equals("post-details")).ToList().ForEach(ebook =>
                    {
                        ebooks.Add(
                            ebook.Descendants("h2").FirstOrDefault(node => node.GetAttributeValue("class", "").Equals("post-title")).Descendants("a").FirstOrDefault().InnerText,
                            ebook.Descendants("a").FirstOrDefault(node => node.GetAttributeValue("class", "").Equals("more-link button")).ChildAttributes("href").FirstOrDefault().Value);
                    });

                    PackageHost.WriteInfo("Accès fiche descriptive pour les livres non deja traités.");
                    ebooks.Where(cur => !dejaConnu.Any(dc => dc.Titre.Equals(cur.Key))).ToList().ForEach(ebook =>
                    {
                        html = httpClient.GetStringAsync(ebook.Value).ConfigureAwait(false).GetAwaiter().GetResult();
                        htmlDoc.LoadHtml(html);

                        var info = htmlDoc.DocumentNode.Descendants("div").SingleOrDefault(node => node.GetAttributeValue("class", "").Equals("entry-content entry clearfix"));
                        info.Descendants("div").Where(node => node.GetAttributeValue("class", "").Contains("stream-item-above-post-content")).ToList().ForEach(cur => cur.Remove());

                        PackageHost.WriteInfo($"Envoi mail pour {ebook.Key}.");
                        SendMail(ebook.Key, info.OuterHtml);
                        ebookCol.InsertOne(new Livre(ebook.Key));
                    });
                });

                PackageHost.WriteInfo("Fin de traitement.");
            }
            catch (Exception ex)
            {
                SendErrorMail("Erreur de traiment NewEbooks", ex.Message);
            }
        }

        /// <summary>
        /// Envoi de l'email
        /// </summary>
        /// <param name="sujet">titre</param>
        /// <param name="contenu">desc</param>
        private void SendMail(string sujet, string contenu)
        {
            var fromAddress = new MailAddress("sebastien.wilhelm@gmail.com", "Ton Homme ;)");
            var toAddress = new MailAddress("sergent.melissa@gmail.com", "Mon amour");
            const string fromPassword = "gmykgxhyzknitgix";

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
            };
            using (var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = sujet,
                Body = contenu,
                IsBodyHtml = true
            })
            {
                smtp.Send(message);
            }
        }

        /// <summary>
        /// Envoi de l'email
        /// </summary>
        /// <param name="sujet">titre</param>
        /// <param name="contenu">desc</param>
        private void SendErrorMail(string sujet, string contenu)
        {
            var address = new MailAddress("sebastien.wilhelm@gmail.com", "Swil");
            const string fromPassword = "gmykgxhyzknitgix";

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(address.Address, fromPassword)
            };
            using (var message = new MailMessage(address, address)
            {
                Subject = sujet,
                Body = contenu,
                IsBodyHtml = true
            })
            {
                smtp.Send(message);
            }
        }

        /// <summary>
        /// Un livre
        /// </summary>
        [BsonIgnoreExtraElements]
        public class Livre
        {
            /// <summary>
            /// Titre du livre
            /// </summary>
            public string Titre { get; }

            /// <summary>
            /// Ctor
            /// </summary>
            /// <param name="titre"></param>
            public Livre(string titre)
            {
                Titre = titre;
            }
        }
    }
}
