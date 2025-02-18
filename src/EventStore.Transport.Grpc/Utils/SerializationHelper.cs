﻿using Newtonsoft.Json;
using Qube.EventStore;
using Serialize.Linq.Serializers;
using System;
using System.Linq.Expressions;
using System.Reactive.Linq;
using JsonSerializer = Serialize.Linq.Serializers.JsonSerializer;

namespace EventStore.Transport.Grpc.Utils
{
    public static class SerializationHelper
    {
        private static readonly JsonSerializer JsonSerializer = new JsonSerializer();

        internal static ExpressionSerializer NewExpressionSerializer(params Type[] knownTypes)
        {
            var expressionSerializer = new ExpressionSerializer(JsonSerializer);
            expressionSerializer.AddKnownType(typeof(StringSplitOptions));  // TODO: Have this come in from above
            return expressionSerializer;
        }

        // TODO: Move away from here
        internal static ParameterExpression NewObserverParameter<T>()
        {
            return Expression.Parameter(typeof(IQbservable<T>), "o");
        }

        internal static string SerializeLinqExpression<TIn, TOut>(Expression expression)
        {
            var expressionSerializer = NewExpressionSerializer
            (
                typeof(StringSplitOptions)
            );

            // TODO: Pull this expression stuff out
            var parameter = NewObserverParameter<TIn>();
            var lambda = Expression.Lambda<Func<IQbservable<TIn>, IQbservable<TOut>>>(expression, parameter);
            var serializedLambda = expressionSerializer.SerializeText(lambda);
            return serializedLambda;
        }

        public static Expression DeserializeLinqExpression(string expressionString)
        {
            var serializer = new ExpressionSerializer(new JsonSerializer());
            var expression = serializer.DeserializeText(expressionString);
            return expression;
        }

        public static EventEnvelope Pack(object payload)
        {
            return new EventEnvelope
            {
                Payload = JsonConvert.SerializeObject(payload)
            };
        }

        public static T Unpack<T>(EventEnvelope @event)
        {
            return typeof(T).Name.Contains("AnonymousType") ?
                JsonConvert.DeserializeAnonymousType(@event.Payload, default(T)) :
                JsonConvert.DeserializeObject<T>(@event.Payload);
        }
    }
}
