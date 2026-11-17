using System;
using System.Collections.Generic;
using System.Linq;

namespace CorporateRag
{
    public sealed class User
    {
        public string Name { get; private set; }
        public string Department { get; private set; }

        public User(string name, string department)
        {
            Name = name;
            Department = department;
        }
    }

    public sealed class Conversation
    {
        private readonly List<Message> messages = new List<Message>();
        public User Owner { get; private set; }
        public IReadOnlyList<Message> Messages { get { return messages.AsReadOnly(); } }

        public Conversation(User owner)
        {
            Owner = owner;
        }

        public Message AddMessage(string role, string text, IEnumerable<string> sources)
        {
            var message = new Message(this, role, text, sources);
            messages.Add(message);
            return message;
        }
    }

    public sealed class Message
    {
        public Conversation Conversation { get; private set; }
        public string Role { get; private set; }
        public string Text { get; private set; }
        public IReadOnlyList<string> Sources { get; private set; }

        internal Message(Conversation conversation, string role, string text, IEnumerable<string> sources)
        {
            Conversation = conversation;
            Role = role;
            Text = text;
            Sources = new List<string>(sources).AsReadOnly();
        }
    }

    public sealed class KnowledgeDocument
    {
        private readonly HashSet<string> departments;
        public string Title { get; private set; }
        public string Text { get; private set; }

        public KnowledgeDocument(string title, string text, params string[] departments)
        {
            if (departments.Length == 0)
                throw new ArgumentException("У документа должна быть область доступа.", "departments");
            Title = title;
            Text = text;
            this.departments = new HashSet<string>(departments, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsAccessibleTo(User user)
        {
            return departments.Contains(user.Department) || departments.Contains("common");
        }

        public bool Matches(string term)
        {
            return Text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public sealed class SearchService
    {
        private readonly IReadOnlyList<KnowledgeDocument> documents;

        public SearchService(IEnumerable<KnowledgeDocument> documents)
        {
            this.documents = new List<KnowledgeDocument>(documents).AsReadOnly();
        }

        public IReadOnlyList<KnowledgeDocument> FindAccessible(User user, string term)
        {
            var results = new List<KnowledgeDocument>();
            foreach (var document in documents)
            {
                if (document.IsAccessibleTo(user) && document.Matches(term))
                    results.Add(document);
            }
            return results.AsReadOnly();
        }
    }

    public abstract class AnswerGenerator
    {
        public abstract string Generate(IReadOnlyList<KnowledgeDocument> context);
    }

    public sealed class ExtractiveGenerator : AnswerGenerator
    {
        public override string Generate(IReadOnlyList<KnowledgeDocument> context)
        {
            return string.Join(Environment.NewLine, context.Select(document => document.Text));
        }
    }

    public sealed class CitationListGenerator : AnswerGenerator
    {
        public override string Generate(IReadOnlyList<KnowledgeDocument> context)
        {
            return "Подходящие материалы: " + string.Join(", ", context.Select(document => document.Title));
        }
    }

    public sealed class ChatService
    {
        public const string NoAnswer = "В доступной базе знаний нет информации для ответа на этот вопрос.";

        public Message SendQuestion(User user, Conversation conversation, string question,
            SearchService search, AnswerGenerator generator)
        {
            if (!ReferenceEquals(user, conversation.Owner))
                throw new UnauthorizedAccessException("Чужой диалог.");
            if (string.IsNullOrWhiteSpace(question))
                throw new ArgumentException("Введите вопрос.", "question");

            var term = question.Trim();
            conversation.AddMessage("user", term, new string[0]);
            var found = search.FindAccessible(user, term);
            if (found.Count == 0)
                return conversation.AddMessage("assistant", NoAnswer, new string[0]);

            var answer = generator.Generate(found);
            var sources = BuildSources(found);
            return conversation.AddMessage("assistant", answer, sources);
        }

        private static IReadOnlyList<string> BuildSources(IReadOnlyList<KnowledgeDocument> found)
        {
            return found.Select(document => document.Title).ToList().AsReadOnly();
        }
    }

    internal static class Program
    {
        private static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var user = new User("Анна", "it");
            var conversation = new Conversation(user);
            var search = new SearchService(new[]
            {
                new KnowledgeDocument("Регламент IT", "Порядок командировки: подайте заявку в Service Desk.", "it"),
                new KnowledgeDocument("Общий регламент", "Срок согласования командировки — два рабочих дня.", "common"),
                new KnowledgeDocument("Регламент Sales", "Порядок командировки: обратитесь к директору по продажам.", "sales")
            });
            var chat = new ChatService();
            Show(chat.SendQuestion(user, conversation, "командировки", search, new ExtractiveGenerator()));
            Show(chat.SendQuestion(user, conversation, "командировки", search, new CitationListGenerator()));
            Show(chat.SendQuestion(user, conversation, "страхование", search, new ExtractiveGenerator()));
            Console.WriteLine("Сообщений в диалоге: " + conversation.Messages.Count);
        }

        private static void Show(Message message)
        {
            Console.WriteLine(message.Text);
            if (message.Sources.Count > 0)
                Console.WriteLine("Источники: " + string.Join(", ", message.Sources));
            Console.WriteLine();
        }
    }
}
