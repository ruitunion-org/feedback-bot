﻿//------------------------------------------------------------------------------
// <auto-generated>
//     Этот код создан программой.
//     Исполняемая версия:4.0.30319.42000
//
//     Изменения в этом файле могут привести к неправильной работе и будут потеряны в случае
//     повторной генерации кода.
// </auto-generated>
//------------------------------------------------------------------------------

namespace RuItUnion.FeedbackBot.Properties {
    using System;
    
    
    /// <summary>
    ///   Класс ресурса со строгой типизацией для поиска локализованных строк и т.д.
    /// </summary>
    // Этот класс создан автоматически классом StronglyTypedResourceBuilder
    // с помощью такого средства, как ResGen или Visual Studio.
    // Чтобы добавить или удалить член, измените файл .ResX и снова запустите ResGen
    // с параметром /str или перестройте свой проект VS.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "17.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class Resources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }
        
        /// <summary>
        ///   Возвращает кэшированный экземпляр ResourceManager, использованный этим классом.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("RuItUnion.FeedbackBot.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Перезаписывает свойство CurrentUICulture текущего потока для всех
        ///   обращений к ресурсу с помощью этого класса ресурса со строгой типизацией.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Команды данной категории повзволяют управлять темами и сообщениями в ней.
        /// </summary>
        public static string Category_Description_Thread {
            get {
                return ResourceManager.GetString("Category_Description_Thread", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Тема.
        /// </summary>
        public static string Category_Name_Thread {
            get {
                return ResourceManager.GetString("Category_Name_Thread", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Закрывает тему.
        /// </summary>
        public static string Command_Description_Close {
            get {
                return ResourceManager.GetString("Command_Description_Close", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Удаляет сообщение в чате с пользователем.
        /// </summary>
        public static string Command_Description_Delete {
            get {
                return ResourceManager.GetString("Command_Description_Delete", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Открывает тему.
        /// </summary>
        public static string Command_Description_Open {
            get {
                return ResourceManager.GetString("Command_Description_Open", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Обновляет названия всех тем в чате.
        /// </summary>
        public static string Command_Description_Sync {
            get {
                return ResourceManager.GetString("Command_Description_Sync", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Пользователь заблокировал бота, пересылка сообщения невозможна.
        /// </summary>
        public static string MessageCopier_BotBanned {
            get {
                return ResourceManager.GetString("MessageCopier_BotBanned", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Изменение сообщений не передается, в случае, если вам необходимо изменить текст сообщения — отправьте новое сообщение.
        /// </summary>
        public static string MessageEditorMiddleware_NotSupported {
            get {
                return ResourceManager.GetString("MessageEditorMiddleware_NotSupported", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Произошла ошибка при пересылке, ваше сообщение не было доставлено.
        /// </summary>
        public static string MessageForwarderMiddleware_Exception {
            get {
                return ResourceManager.GetString("MessageForwarderMiddleware_Exception", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Привет! Этот бот перенаправит все ваши сообщения в чат операторам, а их ответы - перенаправит вам..
        /// </summary>
        public static string StartMessage {
            get {
                return ResourceManager.GetString("StartMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Данное сообщение не было отправлено пользователю, его достаточно удалить стандартными средствами Telegram.
        /// </summary>
        public static string ThreadController_Delete_NotFound {
            get {
                return ResourceManager.GetString("ThreadController_Delete_NotFound", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Данную команду необходимо использовать, отвечая ей на сообщение.
        /// </summary>
        public static string ThreadController_Delete_NotReply {
            get {
                return ResourceManager.GetString("ThreadController_Delete_NotReply", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на Информация о пользователе:
        ///
        ///Имя: {0};
        ///Фамилия: {1};
        ///Имя пользователя: {2};
        ///ИД: &lt;a href=&quot;tg://openmessage?user_id={3:D}&quot;&gt;{3:D}&lt;/a&gt;;
        ///Язык: {4}..
        /// </summary>
        public static string UserInfoMessage {
            get {
                return ResourceManager.GetString("UserInfoMessage", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Ищет локализованную строку, похожую на —.
        /// </summary>
        public static string UserInfoMessage_NoData {
            get {
                return ResourceManager.GetString("UserInfoMessage_NoData", resourceCulture);
            }
        }
    }
}
