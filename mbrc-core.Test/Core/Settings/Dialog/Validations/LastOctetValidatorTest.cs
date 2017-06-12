using MusicBeeRemote.Core.Settings.Dialog.Validations;
using NUnit.Framework;

namespace mbrc_core.Test.Core.Settings.Dialog.Validations
{
    [TestFixture]
    public class LastOctetValidatorTest
    {
        private LastOctetValidator _validator;
        
        [SetUp]
        public void Before()
        {
            _validator = new LastOctetValidator();
        }
        
        [Test]
        public void Invalid_LastOctetZero()
        {
            Assert.IsFalse(_validator.Validate("192.168.1.10", "0"));           
        }

        [Test]
        public void Invalid_LastOctetOverMax()
        {
            Assert.IsFalse(_validator.Validate("192.168.1.10", "255"));
        }
        
        [Test]
        public void Invalid_LastLessThanLastOfBaseIp()
        {
            Assert.IsFalse(_validator.Validate("192.168.1.10", "8"));
        }
        
        [Test]
        public void Valid_LastOctetValid()
        {
            Assert.IsTrue(_validator.Validate("192.168.1.10", "20"));
        }

        [Test]
        public void Invalid_BothInputsNull()
        {
            Assert.IsFalse(_validator.Validate(null, null));
        }
        
        [Test]
        public void Invalid_BothInputsEmpty()
        {
            Assert.IsFalse(_validator.Validate("",""));
        }
             
    }
}
