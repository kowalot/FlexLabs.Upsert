﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FlexLabs.EntityFrameworkCore.Upsert.Runners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace FlexLabs.EntityFrameworkCore.Upsert
{
    public class UpsertCommandBuilder<TEntity> where TEntity : class
    {
        private readonly DbContext _dbContext;
        private readonly IEntityType _entityType;
        private readonly TEntity[] _entities;
        private IList<IProperty> _joinColumns;
        private IList<(IProperty Property, KnownExpressions Value)> _updateExpressions;
        private IList<(IProperty Property, object Value)> _updateValues;

        internal UpsertCommandBuilder(DbContext dbContext, TEntity[] entities)
        {
            _dbContext = dbContext;
            _entities = entities ?? throw new ArgumentNullException(nameof(entities));

            _entityType = dbContext.GetService<IModel>().FindEntityType(typeof(TEntity));
        }

        public UpsertCommandBuilder<TEntity> On(Expression<Func<TEntity, object>> match)
        {
            if (_joinColumns != null)
                throw new InvalidOperationException($"Can't call {nameof(On)} twice!");
            if (match == null)
                throw new ArgumentNullException(nameof(match));

            if (match.Body is NewExpression newExpression)
            {
                _joinColumns = new List<IProperty>();
                foreach (MemberExpression arg in newExpression.Arguments)
                {
                    if (arg == null || !(arg.Member is PropertyInfo) || !typeof(TEntity).Equals(arg.Expression.Type))
                        throw new InvalidOperationException("Match columns have to be properties of the TEntity class");
                    var property = _entityType.FindProperty(arg.Member.Name);
                    if (property == null)
                        throw new InvalidOperationException("Unknown property " + arg.Member.Name);
                    _joinColumns.Add(property);
                }
            }
            else if (match.Body is UnaryExpression unaryExpression)
            {
                if (!(unaryExpression.Operand is MemberExpression memberExp) || !typeof(TEntity).Equals(memberExp.Expression.Type))
                    throw new InvalidOperationException("Match columns have to be properties of the TEntity class");
                var property = _entityType.FindProperty(memberExp.Member.Name);
                _joinColumns = new List<IProperty> { property };
            }
            else if (match.Body is MemberExpression memberExpression)
            {
                if (!typeof(TEntity).Equals(memberExpression.Expression.Type))
                    throw new InvalidOperationException("Match columns have to be properties of the TEntity class");
                var property = _entityType.FindProperty(memberExpression.Member.Name);
                _joinColumns = new List<IProperty> { property };
            }
            else
            {
                throw new ArgumentException("match must be an anonymous object initialiser", nameof(match));
            }

            return this;
        }

        public UpsertCommandBuilder<TEntity> UpdateColumns(Expression<Func<TEntity, TEntity>> updater)
        {
            if (_updateValues != null)
                throw new InvalidOperationException($"Can't call {nameof(UpdateColumns)} twice!");
            if (updater == null)
                throw new ArgumentNullException(nameof(updater));
            if (!(updater.Body is MemberInitExpression entityUpdater))
                throw new ArgumentException("updater must be an Initialiser of the TEntity type", nameof(updater));

            _updateExpressions = new List<(IProperty, KnownExpressions)>();
            _updateValues = new List<(IProperty, object)>();
            foreach (MemberAssignment binding in entityUpdater.Bindings)
            {
                var property = _entityType.FindProperty(binding.Member.Name);
                if (property == null)
                    throw new InvalidOperationException("Unknown property " + binding.Member.Name);
                var value = binding.Expression.GetValue();
                if (value is KnownExpressions knownExp && typeof(TEntity).Equals(knownExp.SourceType) && knownExp.SourceProperty == binding.Member.Name)
                    _updateExpressions.Add((property, knownExp));
                else
                    _updateValues.Add((property, value));
            }

            return this;
        }

        private IUpsertCommandRunner GetCommandRunner()
        {
            var dbProvider = _dbContext.GetService<IDatabaseProvider>();
            var commandRunner = _dbContext.GetInfrastructure().GetServices<IUpsertCommandRunner>()
                .Concat(DefaultRunners.Generators)
                .FirstOrDefault(r => r.Supports(dbProvider.Name));
            if (commandRunner == null)
                throw new NotSupportedException("Database provider not supported yet!");

            return commandRunner;
        }

        public void Run()
        {
            var commandRunner = GetCommandRunner();
            commandRunner.Run(_dbContext, _entityType, _entities, _joinColumns, _updateExpressions, _updateValues);
        }

        public Task RunAsync() => RunAsync(CancellationToken.None);

        public Task RunAsync(CancellationToken token = default)
        {
            var commandRunner = GetCommandRunner();
            return commandRunner.RunAsync(_dbContext, _entityType, _entities, _joinColumns, _updateExpressions, _updateValues, token);
        }
    }
}
