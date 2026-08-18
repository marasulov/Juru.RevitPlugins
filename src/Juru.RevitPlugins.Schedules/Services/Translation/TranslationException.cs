using System;

namespace Juru.RevitPlugins.Schedules.Services.Translation;

/// <summary>
///     Служба перевода не смогла выполнить запрос или вернула неожидаемый ответ.
/// </summary>
public sealed class TranslationException(string message) : Exception(message);
