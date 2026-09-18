using Birko.Data.Patterns.UnitOfWork;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.Tests.UnitOfWork;

public class UnitOfWorkExceptionTests
{
    [Fact]
    public void UnitOfWorkException_Message_IsSet()
    {
        var ex = new UnitOfWorkException("test error");
        ex.Message.Should().Be("test error");
    }

    [Fact]
    public void UnitOfWorkException_InnerException_IsSet()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new UnitOfWorkException("outer", inner);

        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void NoActiveTransactionException_Message_ContainsBeginAsync()
    {
        var ex = new NoActiveTransactionException();
        ex.Message.Should().Contain("BeginAsync");
    }

    [Fact]
    public void TransactionAlreadyActiveException_Message_ContainsAlreadyActive()
    {
        var ex = new TransactionAlreadyActiveException();
        ex.Message.Should().Contain("already active");
    }

    [Fact]
    public void NoActiveTransactionException_InheritsUnitOfWorkException()
    {
        var ex = new NoActiveTransactionException();
        ex.Should().BeAssignableTo<UnitOfWorkException>();
    }

    [Fact]
    public void TransactionAlreadyActiveException_InheritsUnitOfWorkException()
    {
        var ex = new TransactionAlreadyActiveException();
        ex.Should().BeAssignableTo<UnitOfWorkException>();
    }
}
